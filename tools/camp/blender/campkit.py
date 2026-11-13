"""Small modeling kit on top of Blender's Python module (bpy), used to build the camp's models from code.

Run with a Python that has `bpy` installed (pip install bpy), no Blender window needed:
    python tools/camp/blender/build_camp.py

Authoring frame: X right, Y away from the camera (depth), Z up, meters.
Unity frame: (x, y, z) -> (x, z, y); triangle winding is flipped on export.

Vertex data that ends up in Unity:
    color.rgb  base color, authored in sRGB
    color.a    baked ambient occlusion (1 = open)
    uv0        texture coordinates where a part needs them
    uv1 (4f)   "data": x = glow channel (see GLOW), y = glow strength, z = gloss, w = free (wind weight on plants)
"""
import math, struct, random, os, json
import bpy, bmesh
import numpy as np
from mathutils import Vector, Matrix, Quaternion, noise

GLOW = {'none': 0, 'fire': 1, 'tent': 2, 'lantern': 3, 'town': 4, 'radio': 5, 'player': 6, 'ember': 7}


def hexc(h, mul=1.0):
    h = h.lstrip('#')
    return tuple(min(1.0, int(h[i:i + 2], 16) / 255.0 * mul) for i in (0, 2, 4))


def mix(a, b, t):
    return tuple(a[i] * (1 - t) + b[i] * t for i in range(3))


def jitter_color(c, rng, value=0.06, hue=0.02):
    v = 1 + rng.uniform(-value, value)
    return tuple(min(1.0, max(0.0, ch * v + rng.uniform(-hue, hue))) for ch in c)


class P:
    """A piece of geometry under construction (a bmesh with color / data / uv layers)."""

    def __init__(self):
        self.bm = bmesh.new()
        self.col = self.bm.loops.layers.float_color.new('Col')
        self.dat = self.bm.loops.layers.float_color.new('Data')
        self.uv = self.bm.loops.layers.uv.new('UV')

    # ---- construction -------------------------------------------------------------------
    @staticmethod
    def from_pydata(verts, faces, color=(0.5, 0.5, 0.5), smooth=False):
        p = P()
        bv = [p.bm.verts.new(v) for v in verts]
        for f in faces:
            try:
                p.bm.faces.new([bv[i] for i in f])
            except ValueError:
                pass
        p.bm.normal_update()
        p.paint(color)
        p.zero_data()
        p.shade(smooth)
        return p

    def zero_data(self):
        for f in self.bm.faces:
            for l in f.loops:
                l[self.dat] = (0.0, 0.0, 0.0, 0.0)
        return self

    def copy(self):
        q = P.__new__(P)
        q.bm = self.bm.copy()
        q.col = q.bm.loops.layers.float_color['Col']
        q.dat = q.bm.loops.layers.float_color['Data']
        q.uv = q.bm.loops.layers.uv['UV']
        return q

    def add(self, other, matrix=None):
        """Merge another piece into this one (optionally transformed)."""
        o = other if matrix is None else other.copy().xform(matrix)
        me = bpy.data.meshes.new('_tmp')
        o.bm.to_mesh(me)
        self.bm.from_mesh(me)
        bpy.data.meshes.remove(me)
        self.col = self.bm.loops.layers.float_color['Col']
        self.dat = self.bm.loops.layers.float_color['Data']
        self.uv = self.bm.loops.layers.uv['UV']
        return self

    # ---- transforms ---------------------------------------------------------------------
    def xform(self, m):
        self.bm.transform(m)
        if m.determinant() < 0:
            bmesh.ops.reverse_faces(self.bm, faces=self.bm.faces[:])
        self.bm.normal_update()
        return self

    def move(self, x=0.0, y=0.0, z=0.0):
        return self.xform(Matrix.Translation((x, y, z)))

    def rot(self, axis, deg):
        return self.xform(Matrix.Rotation(math.radians(deg), 4, axis))

    def scale(self, sx, sy=None, sz=None):
        sy = sx if sy is None else sy
        sz = sx if sz is None else sz
        return self.xform(Matrix.Diagonal((sx, sy, sz, 1)))

    def displace(self, fn):
        """fn(co: Vector, normal: Vector) -> new position."""
        self.bm.normal_update()
        new = [fn(v.co.copy(), v.normal.copy()) for v in self.bm.verts]
        for v, c in zip(self.bm.verts, new):
            v.co = c
        self.bm.normal_update()
        return self

    # ---- attributes ---------------------------------------------------------------------
    def _faces(self, where):
        return self.bm.faces if where is None else [f for f in self.bm.faces if where(f)]

    def paint(self, color, where=None, ao=1.0):
        c = (color[0], color[1], color[2], ao)
        for f in self._faces(where):
            for l in f.loops:
                l[self.col] = c
        return self

    def paint_fn(self, fn):
        """fn(face, loop) -> (r, g, b)."""
        for f in self.bm.faces:
            for l in f.loops:
                c = fn(f, l)
                l[self.col] = (c[0], c[1], c[2], l[self.col][3])
        return self

    def data(self, glow=None, strength=None, gloss=None, w=None, where=None):
        for f in self._faces(where):
            for l in f.loops:
                d = list(l[self.dat])
                if glow is not None: d[0] = float(GLOW[glow]) if isinstance(glow, str) else float(glow)
                if strength is not None: d[1] = strength
                if gloss is not None: d[2] = gloss
                if w is not None: d[3] = w
                l[self.dat] = d
        return self

    def shade(self, smooth=True, where=None):
        for f in self._faces(where):
            f.smooth = smooth
        return self

    def sharp(self, angle_deg=40.0):
        """Smooth shading with hard edges where faces meet at more than the angle."""
        self.bm.normal_update()
        lim = math.radians(angle_deg)
        for f in self.bm.faces:
            f.smooth = True
        for e in self.bm.edges:
            if len(e.link_faces) == 2:
                e.smooth = e.link_faces[0].normal.angle(e.link_faces[1].normal, 0.0) < lim
            else:
                e.smooth = False
        return self

    def planar_uv(self, origin=(0, 0, 0), u=(1, 0, 0), v=(0, 1, 0), scale=1.0, where=None):
        o, u, v = Vector(origin), Vector(u), Vector(v)
        for f in self._faces(where):
            for l in f.loops:
                d = l.vert.co - o
                l[self.uv].uv = (d.dot(u) * scale, d.dot(v) * scale)
        return self

    # ---- mesh operations ----------------------------------------------------------------
    def bevel(self, offset, segments=1, min_angle_deg=25.0, profile=0.5):
        self.bm.normal_update()
        lim = math.radians(min_angle_deg)
        edges = [e for e in self.bm.edges if len(e.link_faces) == 2 and e.link_faces[0].normal.angle(e.link_faces[1].normal, 0.0) > lim]
        if edges:
            bmesh.ops.bevel(self.bm, geom=edges, offset=offset, segments=segments, profile=profile, affect='EDGES', clamp_overlap=True)
        self.bm.normal_update()
        return self

    def subdivide(self, cuts=1, smooth=0.0):
        bmesh.ops.subdivide_edges(self.bm, edges=self.bm.edges[:], cuts=cuts, use_grid_fill=True, smooth=smooth)
        self.bm.normal_update()
        return self

    def subsurf(self, levels=1):
        """Catmull-Clark through a temporary object (keeps colors and data)."""
        me = bpy.data.meshes.new('_ss')
        self.bm.to_mesh(me)
        ob = bpy.data.objects.new('_ss', me)
        bpy.context.scene.collection.objects.link(ob)
        mod = ob.modifiers.new('ss', 'SUBSURF')
        mod.levels = levels
        mod.render_levels = levels
        dg = bpy.context.evaluated_depsgraph_get()
        ev = ob.evaluated_get(dg)
        me2 = bpy.data.meshes.new_from_object(ev)
        self.bm.free()
        self.bm = bmesh.new()
        self.bm.from_mesh(me2)
        self.col = self.bm.loops.layers.float_color['Col']
        self.dat = self.bm.loops.layers.float_color['Data']
        self.uv = self.bm.loops.layers.uv['UV']
        bpy.data.objects.remove(ob)
        bpy.data.meshes.remove(me)
        bpy.data.meshes.remove(me2)
        self.bm.normal_update()
        return self

    def triangulate(self):
        bmesh.ops.triangulate(self.bm, faces=self.bm.faces[:])
        return self

    def weld(self, dist=1e-4):
        bmesh.ops.remove_doubles(self.bm, verts=self.bm.verts[:], dist=dist)
        return self

    def solidify(self, thickness):
        """Give a sheet thickness (extrude along normals, keep both sides)."""
        self.bm.normal_update()
        geom = bmesh.ops.solidify(self.bm, geom=self.bm.faces[:], thickness=thickness)
        self.bm.normal_update()
        return self

    def bounds(self):
        xs = [v.co for v in self.bm.verts]
        lo = Vector((min(c.x for c in xs), min(c.y for c in xs), min(c.z for c in xs)))
        hi = Vector((max(c.x for c in xs), max(c.y for c in xs), max(c.z for c in xs)))
        return lo, hi

    # ---- output -------------------------------------------------------------------------
    def to_object(self, name):
        me = bpy.data.meshes.new(name)
        self.bm.to_mesh(me)
        ob = bpy.data.objects.new(name, me)
        bpy.context.scene.collection.objects.link(ob)
        return ob


# ======================================================================== primitives

def box(sx, sy, sz, color=(0.5, 0.5, 0.5), bevel=0.0, segments=1):
    """Box centered on the origin."""
    p = P()
    bmesh.ops.create_cube(p.bm, size=1.0)
    p.scale(sx, sy, sz)
    p.paint(color)
    p.zero_data()
    if bevel > 0:
        p.bevel(bevel, segments, 10)
    p.sharp(35)
    return p


def cylinder(r1, r2, h, n=12, color=(0.5, 0.5, 0.5), caps=True, smooth=True):
    """Along Z, from z=0 to z=h. r1 at the bottom, r2 at the top."""
    p = P()
    bmesh.ops.create_cone(p.bm, cap_ends=caps, cap_tris=False, segments=n, radius1=r1, radius2=r2, depth=h)
    p.move(0, 0, h / 2)
    p.paint(color)
    p.zero_data()
    if smooth:
        p.sharp(50)
    return p


def lathe(profile, n=16, color=(0.5, 0.5, 0.5), cap_bottom=True, cap_top=True, angle=40):
    """Surface of revolution around Z. profile: list of (radius, z) from bottom to top."""
    verts, faces = [], []
    m = len(profile)
    for (r, z) in profile:
        for i in range(n):
            a = 2 * math.pi * i / n
            verts.append((r * math.cos(a), r * math.sin(a), z))
    for j in range(m - 1):
        for i in range(n):
            a, b = j * n + i, j * n + (i + 1) % n
            faces.append((a, b, b + n, a + n))
    if cap_bottom and profile[0][0] > 1e-6:
        faces.append(tuple(reversed(range(n))))
    if cap_top and profile[-1][0] > 1e-6:
        faces.append(tuple(range((m - 1) * n, m * n)))
    p = P.from_pydata(verts, faces, color)
    p.weld(1e-6)
    p.sharp(angle)
    return p


def tube(points, radius, sides=8, color=(0.5, 0.5, 0.5), caps=True, closed=False, angle=60):
    """Sweep a circle along a polyline. radius: number or list per point."""
    pts = [Vector(q) for q in points]
    n = len(pts)
    rad = radius if isinstance(radius, (list, tuple)) else [radius] * n
    verts, faces = [], []
    # parallel transport frame
    tangents = []
    for i in range(n):
        a = pts[(i - 1) % n] if (closed or i > 0) else pts[i]
        b = pts[(i + 1) % n] if (closed or i < n - 1) else pts[i]
        t = (b - a)
        tangents.append(t.normalized() if t.length > 1e-9 else Vector((0, 0, 1)))
    t0 = tangents[0]
    ref = Vector((0, 0, 1)) if abs(t0.z) < 0.9 else Vector((1, 0, 0))
    u = t0.cross(ref).normalized()
    for i in range(n):
        t = tangents[i]
        if i > 0:
            axis = tangents[i - 1].cross(t)
            if axis.length > 1e-8:
                q = Quaternion(axis.normalized(), tangents[i - 1].angle(t, 0.0))
                u = q @ u
        u = (u - t * u.dot(t)).normalized()
        v = t.cross(u)
        for k in range(sides):
            a = 2 * math.pi * k / sides
            verts.append(tuple(pts[i] + (u * math.cos(a) + v * math.sin(a)) * rad[i]))
    rings = n if closed else n - 1
    for i in range(rings):
        for k in range(sides):
            a, b = i * sides + k, i * sides + (k + 1) % sides
            c, d = ((i + 1) % n) * sides + (k + 1) % sides, ((i + 1) % n) * sides + k
            faces.append((a, b, c, d))
    if caps and not closed:
        faces.append(tuple(reversed(range(sides))))
        faces.append(tuple(range((n - 1) * sides, n * sides)))
    p = P.from_pydata(verts, faces, color)
    p.sharp(angle)
    return p


def ico(radius=1.0, subdiv=2, color=(0.5, 0.5, 0.5)):
    p = P()
    bmesh.ops.create_icosphere(p.bm, subdivisions=subdiv, radius=radius)
    p.paint(color)
    p.zero_data()
    p.shade(True)
    return p


def grid(sx, sy, nx, ny, color=(0.5, 0.5, 0.5)):
    """Flat grid in XY centered at the origin, facing +Z."""
    verts = [((i / nx - .5) * sx, (j / ny - .5) * sy, 0) for j in range(ny + 1) for i in range(nx + 1)]
    faces = [(j * (nx + 1) + i, j * (nx + 1) + i + 1, (j + 1) * (nx + 1) + i + 1, (j + 1) * (nx + 1) + i) for j in range(ny) for i in range(nx)]
    return P.from_pydata(verts, faces, color, smooth=True)


def hull(points, color=(0.5, 0.5, 0.5)):
    p = P()
    vs = [p.bm.verts.new(q) for q in points]
    res = bmesh.ops.convex_hull(p.bm, input=vs)
    junk = [g for g in res.get('geom_interior', []) if isinstance(g, bmesh.types.BMVert)]
    junk += [g for g in res.get('geom_unused', []) if isinstance(g, bmesh.types.BMVert)]
    if junk:
        bmesh.ops.delete(p.bm, geom=list(set(junk)), context='VERTS')
    bmesh.ops.recalc_face_normals(p.bm, faces=p.bm.faces[:])
    p.bm.normal_update()
    p.paint(color)
    p.zero_data()
    return p


def polygon_prism(outline, z0, z1, color=(0.5, 0.5, 0.5)):
    """Extrude a 2D outline [(x, y)...] (counter-clockwise seen from +Z) between z0 and z1."""
    n = len(outline)
    verts = [(x, y, z0) for x, y in outline] + [(x, y, z1) for x, y in outline]
    faces = [tuple(reversed(range(n))), tuple(range(n, 2 * n))]
    faces += [(i, (i + 1) % n, n + (i + 1) % n, n + i) for i in range(n)]
    return P.from_pydata(verts, faces, color)


# ======================================================================== noise helpers

def fbm(x, y, z=0.0, octaves=4, lac=2.0, gain=0.5):
    """Smooth noise in roughly [-1, 1]."""
    s, a, f = 0.0, 0.5, 1.0
    for _ in range(octaves):
        s += a * noise.noise(Vector((x * f, y * f, z * f)))
        f *= lac
        a *= gain
    return s * 2.0


def sstep(a, b, x):
    if a == b:
        return 0.0 if x < a else 1.0
    t = min(1.0, max(0.0, (x - a) / (b - a)))
    return t * t * (3 - 2 * t)


# ======================================================================== scene and export

class Scene:
    """Collects finished objects, bakes ambient occlusion, writes the Unity files, renders previews."""

    def __init__(self):
        bpy.ops.wm.read_factory_settings(use_empty=True)
        self.items = []      # dict(name, ob, material, group, layer, shadows, anchor)
        self.rng = random.Random(20261113)

    def put(self, name, piece, material='Prop', pos=(0, 0, 0), rot_z=0.0, group=None, shadows=True, layer=None, ao=True, anchor=None, scale=1.0, occluder=True):
        piece.triangulate()
        ob = piece.to_object(name)
        ob.location = pos
        ob.rotation_euler = (0, 0, math.radians(rot_z))
        ob.scale = (scale, scale, scale)
        item = dict(name=name, ob=ob, material=material, group=group or name, layer=layer, shadows=shadows, ao=ao, anchor=anchor, occluder=occluder, raw=getattr(piece, 'raw', None))
        self.items.append(item)
        return item

    # ---- ambient occlusion --------------------------------------------------------------
    def bake_ao(self, samples=24, distance=0.6, only=None):
        sc = bpy.context.scene
        sc.render.engine = 'CYCLES'
        sc.cycles.device = 'CPU'
        sc.cycles.samples = samples
        if sc.world is None:
            sc.world = bpy.data.worlds.new('w')
        sc.world.light_settings.distance = distance
        for it in self.items:
            it['ob'].hide_render = not it['occluder']
        bpy.context.view_layer.update()
        for it in self.items:
            if not it['ao'] or (only and it['name'] not in only):
                continue
            ob = it['ob']
            me = ob.data
            if 'AO' in me.color_attributes:
                me.color_attributes.remove(me.color_attributes['AO'])
            ca = me.color_attributes.new('AO', 'FLOAT_COLOR', 'CORNER')
            me.color_attributes.active_color = ca
            me.color_attributes.render_color_index = list(me.color_attributes).index(ca)
            for o in bpy.context.scene.objects:
                o.select_set(False)
            ob.select_set(True)
            bpy.context.view_layer.objects.active = ob
            try:
                bpy.ops.object.bake(type='AO', target='VERTEX_COLORS')
            except Exception as e:  # noqa
                print('AO bake failed for', it['name'], e)
                continue
            n = len(me.loops)
            ao = np.empty(n * 4, np.float32)
            me.color_attributes['AO'].data.foreach_get('color', ao)
            col = np.empty(n * 4, np.float32)
            me.color_attributes['Col'].data.foreach_get('color', col)
            col = col.reshape(n, 4)
            col[:, 3] = np.clip(ao.reshape(n, 4)[:, 0], 0, 1)
            me.color_attributes['Col'].data.foreach_set('color', col.ravel())
        for it in self.items:
            it['ob'].hide_render = False
        return self

    def bake_ao_image(self, item, size=1024, samples=48, distance=0.7):
        """Ambient occlusion of one object into an image through its UV map; returns a (size, size) array, row 0 = v 0."""
        sc = bpy.context.scene
        sc.render.engine = 'CYCLES'; sc.cycles.device = 'CPU'; sc.cycles.samples = samples
        if sc.world is None:
            sc.world = bpy.data.worlds.new('w')
        sc.world.light_settings.distance = distance
        for it in self.items:
            it['ob'].hide_render = not it['occluder']
        ob = item['ob']
        img = bpy.data.images.new('_ao', size, size, alpha=False, float_buffer=True)
        mat = bpy.data.materials.new('_bake')
        mat.use_nodes = True
        node = mat.node_tree.nodes.new('ShaderNodeTexImage')
        node.image = img
        mat.node_tree.nodes.active = node
        ob.data.materials.clear()
        ob.data.materials.append(mat)
        for o in sc.objects:
            o.select_set(False)
        ob.select_set(True)
        bpy.context.view_layer.objects.active = ob
        bpy.context.view_layer.update()
        bpy.ops.object.bake(type='AO', target='IMAGE_TEXTURES', margin=2)
        px = np.empty(size * size * 4, np.float32)
        img.pixels.foreach_get(px)
        ob.data.materials.clear()
        bpy.data.materials.remove(mat)
        bpy.data.images.remove(img)
        for it in self.items:
            it['ob'].hide_render = False
        return px.reshape(size, size, 4)[:, :, 0]

    # ---- export -------------------------------------------------------------------------
    @staticmethod
    def _mesh_arrays(ob):
        me = ob.data
        me.calc_loop_triangles()
        nl = len(me.loops)
        nv = len(me.vertices)
        co = np.empty(nv * 3, np.float32); me.vertices.foreach_get('co', co); co = co.reshape(nv, 3)
        lv = np.empty(nl, np.int32); me.loops.foreach_get('vertex_index', lv)
        nor = np.empty(nl * 3, np.float32); me.corner_normals.foreach_get('vector', nor); nor = nor.reshape(nl, 3)
        col = np.ones((nl, 4), np.float32)
        if 'Col' in me.color_attributes:
            tmp = np.empty(nl * 4, np.float32); me.color_attributes['Col'].data.foreach_get('color', tmp); col = tmp.reshape(nl, 4)
        dat = np.zeros((nl, 4), np.float32)
        if 'Data' in me.color_attributes:
            tmp = np.empty(nl * 4, np.float32); me.color_attributes['Data'].data.foreach_get('color', tmp); dat = tmp.reshape(nl, 4)
        uv = np.zeros((nl, 2), np.float32)
        if me.uv_layers:
            tmp = np.empty(nl * 2, np.float32); me.uv_layers[0].data.foreach_get('uv', tmp); uv = tmp.reshape(nl, 2)
        nt = len(me.loop_triangles)
        tl = np.empty(nt * 3, np.int32); me.loop_triangles.foreach_get('loops', tl)
        pos = co[lv]
        return pos, nor, col, dat, uv, tl

    def export(self, mesh_path, manifest_path):
        """One binary with every mesh, one json with where each goes. Objects of the same group are merged."""
        groups = {}
        for it in self.items:
            groups.setdefault(it['group'], []).append(it)
        blobs, manifest = [], []
        total_tris = 0
        for gname, its in groups.items():
            first = its[0]
            base = first['ob'].matrix_world.copy() if len(its) == 1 else Matrix.Identity(4)
            P_, N_, C_, D_, U_, I_ = [], [], [], [], [], []
            off = 0
            for it in its:
                ob = it['ob']
                bpy.context.view_layer.update()
                if it.get('raw') is not None:      # plants: arrays as built (placed at the origin)
                    r = it['raw']
                    pos, nor, col, dat, uv, tl = r['pos'], r['nor'], r['col'], r['dat'], r['uv'], r['tri']
                else:
                    pos, nor, col, dat, uv, tl = self._mesh_arrays(ob)
                if len(its) > 1:
                    m = np.array(ob.matrix_world, np.float32)
                    pos = pos @ m[:3, :3].T + m[:3, 3]
                    nm = np.array(ob.matrix_world.to_3x3().inverted().transposed(), np.float32)
                    nor = nor @ nm.T
                    nor /= np.maximum(np.linalg.norm(nor, axis=1, keepdims=True), 1e-9)
                P_.append(pos); N_.append(nor); C_.append(col); D_.append(dat); U_.append(uv); I_.append(tl + off)
                off += len(pos)
            pos, nor, col, dat, uv, idx = map(np.concatenate, (P_, N_, C_, D_, U_, I_))
            # authoring frame -> Unity frame
            pos = pos[:, [0, 2, 1]]; nor = nor[:, [0, 2, 1]]
            col8 = (np.clip(col, 0, 1) * 255 + .5).astype(np.uint8)
            # weld identical corners
            key = np.concatenate([np.round(pos, 5), np.round(nor, 3), col8.astype(np.float32), np.round(dat, 4), np.round(uv, 5)], axis=1)
            _, first_idx, inverse = np.unique(key, axis=0, return_index=True, return_inverse=True)
            inverse = inverse.ravel()
            pos, nor, col8, dat, uv = pos[first_idx], nor[first_idx], col8[first_idx], dat[first_idx], uv[first_idx]
            idx = inverse[idx].reshape(-1, 3)[:, [0, 2, 1]].astype(np.uint32)   # flip winding
            nm = gname.encode('utf-8')
            blob = struct.pack('<H', len(nm)) + nm + struct.pack('<II', len(pos), idx.size)
            blob += pos.astype('<f4').tobytes() + nor.astype('<f4').tobytes() + col8.tobytes() + uv.astype('<f4').tobytes() + dat.astype('<f4').tobytes() + idx.astype('<u4').tobytes()
            blobs.append(blob)
            total_tris += len(idx)
            loc, rot, scl = base.decompose()
            manifest.append(dict(
                name=gname, mesh=gname, material=first['material'],
                position=[loc.x, loc.z, loc.y],
                # rotation about the vertical axis only (that is all the kit uses)
                yaw=-math.degrees(base.to_euler('XYZ').z),
                scale=scl.x, shadows=first['shadows'], layer=first['layer'] or '', anchor=first['anchor'] or '',
                vertices=int(len(pos)), triangles=int(len(idx))))
        os.makedirs(os.path.dirname(mesh_path), exist_ok=True)
        import gzip
        with gzip.GzipFile(mesh_path, 'wb', compresslevel=9, mtime=0) as f:      # the importer unzips it
            f.write(b'IOM1' + struct.pack('<I', len(blobs)))
            for b in blobs:
                f.write(b)
        with open(manifest_path, 'w', encoding='utf-8') as f:
            json.dump(dict(objects=manifest), f, ensure_ascii=False, indent=1)
        print(f'exported {len(blobs)} meshes, {total_tris} triangles -> {mesh_path}')

    # ---- preview ------------------------------------------------------------------------
    def preview(self, path, cam_pos, cam_target, fov_v=50.0, res=(540, 1200), samples=48, lights=(), sky=((0.30, 0.42, 0.86), (0.92, 0.63, 0.84)), sky_strength=0.25, clay=False):
        """Quick Cycles render to check shapes and layout (not the final look; that is Unity's job)."""
        sc = bpy.context.scene
        mat = bpy.data.materials.get('_preview') or bpy.data.materials.new('_preview')
        mat.use_nodes = True
        nt = mat.node_tree
        nt.nodes.clear()
        out = nt.nodes.new('ShaderNodeOutputMaterial')
        bsdf = nt.nodes.new('ShaderNodeBsdfPrincipled')
        att = nt.nodes.new('ShaderNodeAttribute'); att.attribute_name = 'Col'
        gam = nt.nodes.new('ShaderNodeGamma'); gam.inputs[1].default_value = 2.2
        dat = nt.nodes.new('ShaderNodeAttribute'); dat.attribute_name = 'Data'
        sep = nt.nodes.new('ShaderNodeSeparateColor')
        mul = nt.nodes.new('ShaderNodeMath'); mul.operation = 'MULTIPLY'; mul.inputs[1].default_value = 6.0
        gt = nt.nodes.new('ShaderNodeMath'); gt.operation = 'GREATER_THAN'; gt.inputs[1].default_value = 0.5
        mul2 = nt.nodes.new('ShaderNodeMath'); mul2.operation = 'MULTIPLY'
        nt.links.new(att.outputs['Color'], gam.inputs[0])
        if clay:
            bsdf.inputs['Base Color'].default_value = (0.6, 0.6, 0.6, 1)
        else:
            nt.links.new(gam.outputs[0], bsdf.inputs['Base Color'])
        nt.links.new(dat.outputs['Color'], sep.inputs[0])
        nt.links.new(sep.outputs[0], gt.inputs[0])
        nt.links.new(sep.outputs[1], mul.inputs[0])
        nt.links.new(gt.outputs[0], mul2.inputs[0]); nt.links.new(mul.outputs[0], mul2.inputs[1])
        nt.links.new(gam.outputs[0], bsdf.inputs['Emission Color'])
        nt.links.new(mul2.outputs[0], bsdf.inputs['Emission Strength'])
        bsdf.inputs['Roughness'].default_value = 0.85
        nt.links.new(bsdf.outputs[0], out.inputs[0])
        for it in self.items:
            me = it['ob'].data
            if not me.materials:
                me.materials.append(mat)
        # sky
        if sc.world is None:
            sc.world = bpy.data.worlds.new('w')
        w = sc.world
        w.use_nodes = True
        wn = w.node_tree
        wn.nodes.clear()
        wo = wn.nodes.new('ShaderNodeOutputWorld'); bg = wn.nodes.new('ShaderNodeBackground')
        tc = wn.nodes.new('ShaderNodeTexCoord'); sx = wn.nodes.new('ShaderNodeSeparateXYZ')
        ramp = wn.nodes.new('ShaderNodeValToRGB')
        ramp.color_ramp.elements[0].position = 0.0; ramp.color_ramp.elements[0].color = (*sky[1], 1)
        ramp.color_ramp.elements[1].position = 0.45; ramp.color_ramp.elements[1].color = (*sky[0], 1)
        wn.links.new(tc.outputs['Generated'], sx.inputs[0]); wn.links.new(sx.outputs['Z'], ramp.inputs[0])
        wn.links.new(ramp.outputs[0], bg.inputs[0]); bg.inputs[1].default_value = sky_strength
        wn.links.new(bg.outputs[0], wo.inputs[0])
        # lights
        for o in [o for o in sc.objects if o.type == 'LIGHT']:
            bpy.data.objects.remove(o)
        for (lp, lc, watts, radius) in lights:
            ld = bpy.data.lights.new('l', 'POINT'); ld.energy = watts; ld.color = lc; ld.shadow_soft_size = radius
            lo = bpy.data.objects.new('l', ld); lo.location = lp; sc.collection.objects.link(lo)
        cam = sc.camera
        if cam is None:
            cd = bpy.data.cameras.new('cam'); cam = bpy.data.objects.new('cam', cd); sc.collection.objects.link(cam); sc.camera = cam
        cam.data.sensor_fit = 'VERTICAL'
        cam.data.angle_y = math.radians(fov_v)
        cam.data.clip_start = 0.05; cam.data.clip_end = 3000
        cam.location = cam_pos
        d = Vector(cam_target) - Vector(cam_pos)
        cam.rotation_euler = d.to_track_quat('-Z', 'Y').to_euler()
        sc.render.engine = 'CYCLES'; sc.cycles.device = 'CPU'; sc.cycles.samples = samples
        sc.cycles.use_denoising = False
        sc.render.resolution_x, sc.render.resolution_y = res
        sc.render.resolution_percentage = 100
        sc.view_settings.view_transform = 'Standard'
        sc.render.filepath = path
        sc.render.image_settings.file_format = 'PNG'
        bpy.ops.render.render(write_still=True)
        print('preview ->', path)
