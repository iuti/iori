using System;
using UnityEngine;

namespace Iori.Camp
{
    public enum CampPropKind { Radio, Player, Lantern, Fire, Iori }

    /// <summary>
    /// A prop in the scene that opens a feature when tapped. The anchor transform it sits on is also
    /// where CampBuilder puts the prop's model. The features' panels are not built yet; listen to
    /// <see cref="Tapped"/> to open them. The lantern toggles sleep mode by itself.
    /// </summary>
    [ExecuteAlways]
    public sealed class CampProp : MonoBehaviour
    {
        public CampPropKind kind;
        [Tooltip("カメラが寄るときに見る点(この物からの位置)")] public Vector3 focusOffset = new Vector3(0, 0.5f, 0);
        [Tooltip("押せる範囲の半径")] public float tapRadius = 0.35f;
        [Tooltip("押したときにカメラが寄るか")] public bool focusOnTap = true;

        public static event Action<CampProp> Tapped;

        public Vector3 FocusPoint => transform.position + focusOffset;

        void OnEnable()
        {
            var c = GetComponent<SphereCollider>();
            if (!c) c = gameObject.AddComponent<SphereCollider>();
            c.isTrigger = true;
            c.center = focusOffset;
            c.radius = tapRadius;
        }

        void OnValidate()
        {
            var c = GetComponent<SphereCollider>();
            if (c) { c.center = focusOffset; c.radius = tapRadius; }
        }

        public void Tap()
        {
            if (kind == CampPropKind.Lantern && CampAtmosphere.Instance)
                CampAtmosphere.Instance.SetSleep(!CampAtmosphere.Instance.sleep);
            Tapped?.Invoke(this);
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.8f, 0.4f, 0.6f);
            Gizmos.DrawWireSphere(FocusPoint, tapRadius);
        }
    }
}
