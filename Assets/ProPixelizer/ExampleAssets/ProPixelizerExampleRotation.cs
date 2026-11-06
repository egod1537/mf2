// Copyright Elliot Bentine, 2018-
using UnityEngine;

namespace ProPixelizer
{
    public class ProPixelizerExampleRotation : MonoBehaviour
    {
        public float Period = 2.2f;
        public bool Counterclockwise = true;
        [Tooltip("If true, rotations will linger around cardinal axes.")]
        public bool Sticky;
        public float StickyWindow = 10f;
        float Angle;

        float GetRotationAngle()
        {
            if (!Sticky)
                return Angle;

            float stickyRegionSize = Mathf.PI / 2;
            float nearestStickyAngle = Mathf.Round(Angle / stickyRegionSize) * stickyRegionSize;
            float prevStickyAngle = Mathf.Floor(Angle / stickyRegionSize) * stickyRegionSize;
            float nextStickyAngle = prevStickyAngle + stickyRegionSize;
            float midPoint = prevStickyAngle + stickyRegionSize / 2.0f;

            // create a quantity that is 0.5 at the mid point, and varies linearly from 0 to 1 from prev+window/2 to next-window/2
            float start = prevStickyAngle + Mathf.Deg2Rad * StickyWindow / 2f;
            float end = nextStickyAngle - Mathf.Deg2Rad * StickyWindow / 2f;

            float linearQuantity = Mathf.Clamp01((Angle - start) / (end - start));

            // Tween between prev and next using the linear quantity
            return Mathf.SmoothStep(prevStickyAngle, nextStickyAngle, linearQuantity);
        }

        // Start is called before the first frame update
        void Start()
        {
            Angle = transform.localRotation.eulerAngles.y;
        }

        // Update is called once per frame
        void Update()
        {
            var sign = Counterclockwise ? 1 : -1;
            Angle += sign * Time.deltaTime * 2.0f * Mathf.PI / Mathf.Abs(Period);
            if (Angle > Mathf.PI * 2.0f)
                Angle -= Mathf.PI * 2.0f;
            if (Angle < 0)
                Angle += Mathf.PI * 2.0f;
            transform.rotation = Quaternion.AngleAxis(Mathf.Rad2Deg * GetRotationAngle(), Vector3.up);
        }
    }
}