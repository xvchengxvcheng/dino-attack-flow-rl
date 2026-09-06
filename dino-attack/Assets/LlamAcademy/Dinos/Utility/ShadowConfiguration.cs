using System.Linq;
using UnityEngine;

namespace LlamAcademy.Dinos.Utility
{
    [RequireComponent(typeof(Light))]
    public class ShadowConfiguration : MonoBehaviour
    {
        [SerializeField] private ShadowBuildTarget[] ShadowsPerPlatform;

        private Light Light;
        private void Awake()
        {
            ShadowBuildTarget target = ShadowsPerPlatform.FirstOrDefault(item => item.Platform == Application.platform);

            if (target != null)
            {
                Light = GetComponent<Light>();
                Light.shadows = target.ShadowMode;
            }
        }

        [System.Serializable]
        public class ShadowBuildTarget
        {
            public RuntimePlatform Platform;
            public LightShadows ShadowMode;
        }
    }
}
