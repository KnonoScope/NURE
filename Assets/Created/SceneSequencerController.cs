using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SceneSequenceController : MonoBehaviour
{
    [System.Serializable]
    public class VirtualScene
    {
        public GameObject root;        // Scena1, Scena2, ...
        public Transform cameraSpawn;  // ScenaX/CameraSpawn
        public AudioClip audio;
    }

    [Header("References")]
    [SerializeField] private Camera mainCamera;
    [SerializeField] private AudioSource audioSource;

    [Header("Scenes Order")]
    [SerializeField] private List<VirtualScene> scenes = new();

    private int currentIndex = -1;

    private void Start()
    {
        // sicurezza
        foreach (var s in scenes)
            if (s.root != null) s.root.SetActive(false);

        PlayNextScene();
    }

    private void PlayNextScene()
    {
        currentIndex++;

        if (currentIndex >= scenes.Count)
        {
            Debug.Log("Sequenza finita");
            return;
        }

        StartCoroutine(PlaySceneRoutine(scenes[currentIndex]));
    }

    private IEnumerator PlaySceneRoutine(VirtualScene scene)
    {
        // 1) Spegni tutto
        foreach (var s in scenes)
            if (s.root != null) s.root.SetActive(false);

        // 2) Accendi scena corrente
        scene.root.SetActive(true);

        // 3) Posiziona camera
        if (mainCamera != null && scene.cameraSpawn != null)
        {
            mainCamera.transform.SetPositionAndRotation(
                scene.cameraSpawn.position,
                scene.cameraSpawn.rotation
            );
        }

        // 4) Audio
        if (audioSource != null && scene.audio != null)
        {
            audioSource.clip = scene.audio;
            audioSource.Play();

            // 5) Aspetta fine audio
            yield return new WaitForSeconds(scene.audio.length);
        }
        else
        {
            yield return new WaitForSeconds(0.1f);
        }

        // 6) Passa alla prossima scena
        PlayNextScene();
    }
}
