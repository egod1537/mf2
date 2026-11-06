// Copyright Elliot Bentine, 2018-
using UnityEngine;

namespace ProPixelizer
{
    public class ProPixelizerExampleBobbing : MonoBehaviour
    {
        public float Period = 2.2f;
        public float AmplitudeDeg = 30f;
        float Phase;

        // Update is called once per frame
        void Update()
        {
            Phase = 2f * Mathf.PI * Time.time / Period;
            var angle = Mathf.Cos(Phase) * AmplitudeDeg;
            transform.rotation = Quaternion.AngleAxis(angle, Vector3.forward);
        }
    }
}