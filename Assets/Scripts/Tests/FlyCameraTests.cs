using NUnit.Framework;
using UnityEngine;
using WFC.Runtime;

namespace WFC.Tests
{
    /// <summary>Ustawienie kamery z kreatorów scen: odległość od celu, kierunek patrzenia i punkt obrotu.</summary>
    public sealed class FlyCameraTests
    {
        [Test]
        public void LookAt_KameraWPodanejOdleglosci_PatrzyNaCel()
        {
            var cameraObject = new GameObject("Kamera testowa");
            try
            {
                var target = new Vector3(30f, 6f, 30f);
                cameraObject.AddComponent<FlyCamera>().LookAt(target, 95f, 30f, 40f);
                Transform transform = cameraObject.transform;

                Assert.AreEqual(95f, Vector3.Distance(transform.position, target), 1e-3f);
                Assert.Less(Vector3.Angle(transform.forward, target - transform.position), 0.01f);
                Assert.Greater(transform.position.y, target.y, "kamera patrzy w dół, więc jest nad celem");
                Assert.Less(Vector3.Distance(target, cameraObject.GetComponent<FlyCamera>().Pivot), 1e-3f, "cel jest punktem obrotu prawego przycisku");
            }
            finally
            {
                Object.DestroyImmediate(cameraObject);
            }
        }
    }
}
