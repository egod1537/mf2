// Copyright Elliot Bentine, 2022-
using UnityEngine;

namespace ProPixelizer
{
    public class ProPixelizerCameraZoom : MonoBehaviour
    {
        public float OrthoSizeMin;
        public float OrthoSizeMax;
        public float Period;
        float time;
        Camera _camera;

        // Start is called before the first frame update
        void Start()
        {
            _camera = GetComponent<Camera>();
        }

        // Update is called once per frame
        void Update()
        {
            time += Time.deltaTime;
            if (time > Period)
                time -= Period;

            float phase = Mathf.PI * time / Period;
            float lerpAmount = Mathf.Pow(Mathf.Cos(phase), 2.0f);
            if (_camera != null)
                _camera.orthographicSize = Mathf.Lerp(OrthoSizeMin, OrthoSizeMax, lerpAmount);
        }
    }
}