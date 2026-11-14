// Draws what PuppetPreview wrote out, the way PuppetRenderer and IoriPuppetLayer.shader do (layer over layer,
// colors as the numbers in the files, the iris cut by the stencil of the eye's white), in a headless Chromium.
// node render.js <out prefix> <model folder> <pictures folder> [--bg r,g,b]
const fs = require('fs'), path = require('path');
const { chromium } = require('playwright');

(async () => {
  const [prefix, modelDir, outDir] = process.argv.slice(2);
  const bgAt = process.argv.indexOf('--bg');
  const bg = bgAt > 0 ? process.argv[bgAt + 1].split(',').map(Number) : null;
  const data = JSON.parse(fs.readFileSync(prefix + '.json', 'utf8'));
  const bin = fs.readFileSync(prefix + '.bin');
  fs.mkdirSync(outDir, { recursive: true });
  const b64 = f => 'data:image/png;base64,' + fs.readFileSync(path.join(modelDir, f)).toString('base64');
  const textures = {};
  for (const L of data.layers) for (const f of [L.texture, L.mask, ...L.frames]) if (f && !textures[f]) textures[f] = b64(f);

  const browser = await chromium.launch({ args: ['--use-angle=swiftshader', '--enable-unsafe-swiftshader', '--ignore-gpu-blocklist'] });
  const page = await browser.newPage();
  await page.evaluate(async ({ canvas, layers, textures, bg }) => {
    const c = document.createElement('canvas'); c.width = canvas[0]; c.height = canvas[1];
    const gl = c.getContext('webgl2', { stencil: true, premultipliedAlpha: true, antialias: false, preserveDrawingBuffer: true });
    const sh = (type, src) => { const s = gl.createShader(type); gl.shaderSource(s, src); gl.compileShader(s); if (!gl.getShaderParameter(s, gl.COMPILE_STATUS)) throw new Error(gl.getShaderInfoLog(s)); return s; };
    const prog = gl.createProgram();
    gl.attachShader(prog, sh(gl.VERTEX_SHADER, `#version 300 es
      in vec2 pos; in vec2 uv; out vec2 vUv; uniform vec2 size;
      void main() { vUv = uv; gl_Position = vec4(pos.x / size.x * 2.0 - 1.0, 1.0 - pos.y / size.y * 2.0, 0.0, 1.0); }`));
    gl.attachShader(prog, sh(gl.FRAGMENT_SHADER, `#version 300 es
      precision highp float; in vec2 vUv; out vec4 o; uniform sampler2D tex; uniform float alpha; uniform float mask;
      void main() {
        vec4 t = texture(tex, vUv);
        vec2 s = step(vec2(0.0), vUv) * step(vUv, vec2(1.0));
        float a = t.a * s.x * s.y;
        if (mask > 0.5) { if (a < 0.5) discard; o = vec4(0.0); return; }
        a *= alpha; o = vec4(t.rgb * a, a);
      }`));
    gl.bindAttribLocation(prog, 0, 'pos'); gl.bindAttribLocation(prog, 1, 'uv');
    gl.linkProgram(prog); gl.useProgram(prog);
    gl.uniform2f(gl.getUniformLocation(prog, 'size'), canvas[0], canvas[1]);
    gl.pixelStorei(gl.UNPACK_PREMULTIPLY_ALPHA_WEBGL, false);
    gl.pixelStorei(gl.UNPACK_COLORSPACE_CONVERSION_WEBGL, gl.NONE);
    const tex = {};
    for (const [name, url] of Object.entries(textures)) {
      const img = new Image(); img.src = url; await img.decode();
      const bmp = await createImageBitmap(img, { premultiplyAlpha: 'none', colorSpaceConversion: 'none' });
      const t = gl.createTexture(); gl.bindTexture(gl.TEXTURE_2D, t);
      gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGBA, gl.RGBA, gl.UNSIGNED_BYTE, bmp);
      gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, gl.LINEAR); gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MAG_FILTER, gl.LINEAR);
      gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_S, gl.CLAMP_TO_EDGE); gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_T, gl.CLAMP_TO_EDGE);
      tex[name] = t;
    }
    const meshes = layers.map(L => {
      const vao = gl.createVertexArray(); gl.bindVertexArray(vao);
      const pb = gl.createBuffer(); gl.bindBuffer(gl.ARRAY_BUFFER, pb); gl.bufferData(gl.ARRAY_BUFFER, L.uv.length * 4, gl.DYNAMIC_DRAW);
      gl.enableVertexAttribArray(0); gl.vertexAttribPointer(0, 2, gl.FLOAT, false, 0, 0);
      const ub = gl.createBuffer(); gl.bindBuffer(gl.ARRAY_BUFFER, ub); gl.bufferData(gl.ARRAY_BUFFER, new Float32Array(L.uv), gl.STATIC_DRAW);
      gl.enableVertexAttribArray(1); gl.vertexAttribPointer(1, 2, gl.FLOAT, false, 0, 0);
      const ib = gl.createBuffer(); gl.bindBuffer(gl.ELEMENT_ARRAY_BUFFER, ib); gl.bufferData(gl.ELEMENT_ARRAY_BUFFER, new Uint32Array(L.tris), gl.STATIC_DRAW);
      return { vao, pb, count: L.tris.length };
    });
    const uAlpha = gl.getUniformLocation(prog, 'alpha'), uMask = gl.getUniformLocation(prog, 'mask');
    window.draw = (draws, b64) => {
      const raw = Uint8Array.from(atob(b64), ch => ch.charCodeAt(0));
      const f = new Float32Array(raw.buffer);
      gl.viewport(0, 0, canvas[0], canvas[1]);
      gl.colorMask(true, true, true, true); gl.disable(gl.STENCIL_TEST);
      if (bg) gl.clearColor(bg[0] / 255, bg[1] / 255, bg[2] / 255, 1); else gl.clearColor(0, 0, 0, 0);
      gl.clearStencil(0); gl.clear(gl.COLOR_BUFFER_BIT | gl.STENCIL_BUFFER_BIT);
      gl.enable(gl.BLEND); gl.blendFunc(gl.ONE, gl.ONE_MINUS_SRC_ALPHA);
      let at = 0;
      for (const [layer, kind, frame, alpha, stencil, ref, , n] of draws) {
        const L = layers[layer], M = meshes[layer];
        const name = kind === 1 ? L.frames[frame] : kind === 2 ? L.mask : L.texture;
        gl.bindVertexArray(M.vao);
        gl.bindBuffer(gl.ARRAY_BUFFER, M.pb); gl.bufferSubData(gl.ARRAY_BUFFER, 0, f.subarray(at, at + n)); at += n;
        gl.bindTexture(gl.TEXTURE_2D, tex[name]);
        gl.uniform1f(uAlpha, alpha); gl.uniform1f(uMask, stencil === 1 ? 1 : 0);
        if (stencil === 1) { gl.colorMask(false, false, false, false); gl.enable(gl.STENCIL_TEST); gl.stencilFunc(gl.ALWAYS, ref, 255); gl.stencilOp(gl.KEEP, gl.KEEP, gl.REPLACE); }
        else if (stencil === 2) { gl.colorMask(true, true, true, true); gl.enable(gl.STENCIL_TEST); gl.stencilFunc(gl.EQUAL, ref, 255); gl.stencilOp(gl.KEEP, gl.KEEP, gl.KEEP); }
        else { gl.colorMask(true, true, true, true); gl.disable(gl.STENCIL_TEST); }
        gl.drawElements(gl.TRIANGLES, M.count, gl.UNSIGNED_INT, 0);
      }
      return c.toDataURL('image/png').slice(22);
    };
  }, { canvas: data.canvas, layers: data.layers, textures, bg });

  for (const fr of data.frames) {
    // the numbers of this picture's draws, in the order they are drawn
    const parts = fr.draws.map(d => bin.subarray(d[6] * 4, (d[6] + d[7]) * 4));
    const png = await page.evaluate(([draws, b]) => window.draw(draws, b), [fr.draws, Buffer.concat(parts).toString('base64')]);
    fs.writeFileSync(path.join(outDir, fr.name + '.png'), Buffer.from(png, 'base64'));
  }
  console.log(data.frames.length + ' pictures in ' + outDir);
  await browser.close();
})().catch(e => { console.error(e); process.exit(1); });
