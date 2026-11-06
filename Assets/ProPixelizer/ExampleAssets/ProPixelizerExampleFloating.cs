// Copyright Elliot Bentine, 2018-
using UnityEngine;

namespace ProPixelizer
{
    public class ProPixelizerExampleFloating : MonoBehaviour
    {
        public float Period = 2.2f;
        public float Amplitude = 1.0f;
        public float Steps = 100f;
        float RotationRate = 0f;
        float PhaseOffset;
        Vector3 Position;
        float Angle;
        Quaternion original;
        public bool Rotates = false;
        public bool RandomStartPhase = true;

        // Start is called before the first frame update
        void Start()
        {
            if (RandomStartPhase)
                PhaseOffset = Random.value;
            else
                PhaseOffset = 0;
            Position = transform.localPosition;
            Angle = Random.value * 360f;
            original = transform.rotation;
            RotationRate = Rotates ? Random.Range(0, 3) * 15f : 0f;
        }

        // Update is called once per frame
        void Update()
        {
            float phase = PhaseOffset + Time.time / Period;
            phase = 2.0f * Mathf.PI * (int)(phase * Steps) / Steps;
            float offset = Amplitude * Mathf.Cos(phase);
            transform.localPosition = offset * Vector3.up + Position;

            Angle += RotationRate * Time.deltaTime;
            if (RotationRate != 0f)
                transform.rotation = Quaternion.AngleAxis(Angle, Vector3.up) * original;
        }
    }
}