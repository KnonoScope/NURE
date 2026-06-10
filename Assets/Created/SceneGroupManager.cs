using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Video;
using UnityEngine.Playables;
using UnityEngine.Animations;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.XR.Interaction.Toolkit.UI;
using UnityEngine.XR;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
#endif

public class SceneGroupManager : MonoBehaviour
{
    public event Action<VirtualScene> SceneActivated;
    public event Action LanguageChanged;

    private const string ForcedScene1Name = "Scena1";
    private const string ForcedScene9Name = "Scena9";
    private const string ForcedScene10Name = "Scena10";
    private const string DefaultGlobalSkyboxTextureResourceName = "rosendal_plains_2_4k";
    private const int RuntimeSkyDomeLongitudeSegments = 64;
    private const int RuntimeSkyDomeLatitudeSegments = 32;
    private const float ContinuousSceneRuleRefreshInterval = 0.2f;
    private const float CameraCacheRefreshInterval = 0.75f;
    private const int DefaultVideoRenderTextureMaxSize = 4096;
    private const int MobileVideoRenderTextureMaxSize = 2048;
    private const int MobileSceneSpawnDissolveMaxRenderers = 8;
    private const float MobileSceneSpawnDissolveDuration = 1.25f;
    private const float MobileSceneSpawnDissolveMinBoundsSize = 0.35f;
    private const float LanguageFlagPlaneWidth = 0.44f;
    private const float LanguageFlagPlaneHeight = 0.26f;
    private const float LanguageFlagPlaneColliderDepth = 0.06f;
    private const int LanguageFlagTextureWidth = 1024;
    private const int LanguageFlagTextureHeight = 512;
    private const string Scene7DelayedDissolveSceneName = "Scena7";
    private const string Scene7DelayedDissolveObjectName = "Mausoleo";
    private const float Scene7DelayedDissolveExtraDelay = 10f;
    private static readonly Color32 ItalianFlagGreen = new Color32(0, 146, 70, 255);
    private static readonly Color32 ItalianFlagWhite = new Color32(244, 245, 240, 255);
    private static readonly Color32 ItalianFlagRed = new Color32(206, 43, 55, 255);
    private static readonly Color32 EnglishFlagBlue = new Color32(1, 33, 105, 255);
    private static readonly Color32 EnglishFlagWhite = new Color32(255, 255, 255, 255);
    private static readonly Color32 EnglishFlagRed = new Color32(200, 16, 46, 255);
    private static readonly string[] ForcedToggleObjectNames =
    {
        "Rune_125K_20x4096",
        "Rune_250K_20x4096 1",
    };
    private const string ProtectedScene1ObjectName = "PlasticoMappa";

    [Serializable]
    public class VirtualScene
    {
        public string name;               // opzionale
        public GameObject root;           // Scena1 / Scena2 / ...
        public Transform playerSpawn;     // spawn (se null, non teleporta)
        public AudioClip audioClip;       // audio scena (opzionale)

        [Header("Optional Video (e.g., Scena10)")]
        public GameObject videoPanelRoot; // pannello/UI da mostrare
        public VideoPlayer videoPlayer;   // VideoPlayer che riproduce il video (con audio)
        public bool waitForVideoEnd = false; // se true, la sequenza aspetta fine video
    }

    public enum NureLanguage
    {
        Italian,
        English
    }

    [Serializable]
    public class LocalizedSceneContent
    {
        public string sceneName;
        public string italianLabel;
        public string englishLabel;
        public AudioClip italianAudioClip;
        public AudioClip englishAudioClip;
    }

    [Header("References")]
    [Tooltip("Assegna qui il Transform ROOT del tuo XR Origin (XR Rig), non la camera.")]
    public Transform player;

    [Tooltip("AudioSource usato per riprodurre i clip delle scene (NON serve per il video).")]
    public AudioSource audioSource;

    [Header("Scenes")]
    public List<VirtualScene> scenes = new List<VirtualScene>();

    [Header("Localization")]
    public bool enableLocalization = true;
    public NureLanguage currentLanguage = NureLanguage.Italian;
    public bool rememberLanguagePreference = true;
    public bool showLanguageSelectionOnStart = true;
    public bool resetToLanguageSelectionOnPause = true;
    public bool showFirstSceneBehindLanguageSelection = false;
    [Tooltip("Se true, la prima scena viene attivata mentre il menu lingua e' visibile, poi nascosta fino alla scelta. Evita il picco di SetActive subito dopo il click lingua.")]
    public bool preactivateFirstSceneDuringLanguageSelection = true;
    [Tooltip("Se true, quando si sceglie la lingua parte sempre la prima scena della lista.")]
    public bool activateFirstSceneAfterLanguageSelection = true;
    public bool disableInitialSceneDissolveAfterLanguageSelection = true;
    public List<LocalizedSceneContent> localizedScenes = new List<LocalizedSceneContent>();

    [Header("Language Selection UI")]
    [Min(0.5f)] public float languagePanelDistanceFromCamera = 1.25f;
    public Vector2 languagePanelSize = new Vector2(900f, 520f);
    public Color languagePanelColor = new Color(0.04f, 0.07f, 0.09f, 0.96f);
    public Color languageButtonColor = new Color(0.14f, 0.36f, 0.46f, 0.98f);
    public Color languageTextColor = new Color(0.98f, 0.96f, 0.9f, 1f);
    public Texture2D italianLanguageFlagTexture;
    public Texture2D englishLanguageFlagTexture;

    public int LanguageVersion { get; private set; }

    [Header("Sequence")]
    public bool playSequenceOnStart = false;

    [Tooltip("Se true, allo switch scena ferma sempre l'audio precedente.")]
    public bool stopAudioOnSwitch = true;

    [Tooltip("Se true, allo switch scena avvia l'audio associato (se presente).")]
    public bool playAudioOnSwitch = true;

    [Tooltip("Se true, aspetta davvero finché audioSource.isPlaying è true. Se false, usa clip.length.")]
    public bool waitUsingIsPlaying = true;

    [Tooltip("Delay iniziale (in secondi) prima di chiamare Play(). Utile in XR per timing della camera/listener.")]
    [Range(0f, 1f)]
    public float audioStartDelay = 0.10f;

    [Tooltip("Delay prima di far partire il video (utile per attivazione pannello/UI).")]
    [Range(0f, 1f)]
    public float videoStartDelay = 0.05f;

    [Tooltip("Se true, stampa log dettagliati in Console.")]
    public bool verboseLogs = true;

    [Header("Scene 1 Visibility")]
    [Tooltip("Legacy: non usato. Lasciato per compatibilita'.")]
    public bool enforceScene1PlasticoOnly = false;

    [Tooltip("Legacy: non usato. Lasciato per compatibilita'.")]
    public string scene1Name = "Scena1";

    [Tooltip("Legacy: non usato. Lasciato per compatibilita'.")]
    public string scene1KeepActiveObjectName = "PlasticoMappa";

    [Tooltip("Se true, gestisce i GO globali delle Rune da spegnere in Scena1/9/10 e accendere nelle altre scene.")]
    public bool enforceGlobalObjectByScene = true;

    [Tooltip("Se true, riafferma la regola ogni frame (utile se altri script cambiano lo stato del GO).")]
    public bool enforceGlobalObjectContinuously = true;

    [Tooltip("Legacy: ignorato. I target sono forzati dalla lista interna delle Rune.")]
    public string globalObjectName = "Rune_125K_20x4096";

    [Header("Skybox by Scene")]
    [Tooltip("Se true, applica uno skybox globale in tutte le scene virtuali.")]
    public bool enforceSkyboxByScene = true;

    [Tooltip("Se true, riafferma la regola skybox ogni frame (utile se altri script cambiano RenderSettings/camera).")]
    public bool enforceSkyboxContinuously = true;

    [Tooltip("Skybox esplicito. Se nullo, viene creato a runtime da Resources usando la texture HDR indicata sotto.")]
    public Material globalSkyboxMaterial;
    [Tooltip("Se true, usa la texture HDR in Resources come sorgente primaria anche se in Inspector e' rimasto un materiale skybox legacy/procedurale.")]
    public bool preferSkyboxTextureResourceOverAssignedMaterial = false;

    [Tooltip("Nome asset in Resources della texture HDR da usare per lo skybox globale (senza estensione).")]
    public string globalSkyboxTextureResourceName = DefaultGlobalSkyboxTextureResourceName;

    [Range(0f, 8f)]
    public float globalSkyboxExposure = 1f;

    [Range(0f, 360f)]
    public float globalSkyboxRotation = 0f;

    [Tooltip("Colore di sfondo usato in Scena1 quando lo skybox e' disattivato.")]
    public Color scene1NoSkyColor = Color.black;

    [Header("Sky Visibility Fallback")]
    [Tooltip("Se true, crea una sky dome runtime con la stessa HDR di Resources per garantire un cielo visibile anche in build Quest/Android.")]
    public bool enableRuntimeSkyDomeFallback = true;

    [Tooltip("Se true, su mobile/Quest mantiene attiva la sky dome anche quando RenderSettings.skybox sembra valido. Di default resta spento per evitare doppio rendering del cielo.")]
    public bool forceRuntimeSkyDomeOnMobile = false;

    [Tooltip("Shader in Resources usato dalla sky dome fallback.")]
    public string runtimeSkyDomeShaderResourceName = "RuntimeSkyDomeUnlit";

    [Min(10f)]
    [Tooltip("Raggio in metri della sky dome runtime.")]
    public float runtimeSkyDomeRadius = 55f;

    [Tooltip("Rotazione extra della sky dome runtime. Y viene sommato alla rotazione skybox globale.")]
    public Vector3 runtimeSkyDomeEulerOffset = Vector3.zero;

    [Range(0.1f, 2f)]
    [Tooltip("Moltiplicatore dedicato alla luminanza della sky dome fallback, utile per Quest quando il dome appare troppo aggressivo rispetto allo skybox standard.")]
    public float runtimeSkyDomeExposureMultiplier = 0.65f;

    [Range(0.1f, 1f)]
    [Tooltip("Riduzione extra dell'esposizione del cielo su mobile/Quest per evitare un output troppo sparato rispetto a Editor/Standalone.")]
    public float mobileSkyExposureMultiplier = 0.7f;

    [Range(0.05f, 1f)]
    [Tooltip("Tetto massimo dell'esposizione della sky dome fallback su mobile/Quest.")]
    public float mobileRuntimeSkyDomeExposureCap = 0.35f;

    [Header("Scene 6 - Donne")]
    [Tooltip("Se true, quando si attiva Scena6 parte: Movimento una volta, poi Idle in loop per ogni donna trovata.")]
    public bool autoPlayDonnaScene6Animation = true;

    [Tooltip("Nome della scena da intercettare nel SceneGroupManager.")]
    public string donnaSceneName = "Scena6";

    [Tooltip("Nome (o parte del nome) del GameObject con Animator della donna.")]
    public string donnaAnimatorObjectName = "Donna_UNo_Arm";

    [Tooltip("Nome (o parte del nome) del secondo GameObject donna (opzionale).")]
    public string donnaSecondAnimatorObjectName = "Donna_DUe";

    [Tooltip("Token da cercare nel nome clip per l'animazione di movimento.")]
    public string donnaMovementToken = "Movimento";

    [Tooltip("Token da cercare nel nome clip per l'animazione idle.")]
    public string donnaIdleToken = "Idle";

    [Tooltip("Token clip preferito per la prima donna.")]
    public string donnaUnoClipToken = "Donna_uno";

    [Tooltip("Token clip preferito per la seconda donna.")]
    public string donnaDueClipToken = "Donna_due";

    [Tooltip("Per Donna Due, idle preferito dopo il movimento.")]
    public string donnaDuePreferredIdleToken = "Idle_Dopo";

    [Header("Scene 6 - Donne (Explicit Clips)")]
    [Tooltip("Clip movimento esplicito per Donna Uno. Se assegnato, ha priorita' rispetto alla ricerca per token.")]
    public AnimationClip donnaUnoMovementClip;

    [Tooltip("Clip idle esplicito per Donna Uno. Se assegnato, ha priorita' rispetto alla ricerca per token.")]
    public AnimationClip donnaUnoIdleClip;

    [Tooltip("Clip movimento esplicito per Donna Due. Se assegnato, ha priorita' rispetto alla ricerca per token.")]
    public AnimationClip donnaDueMovementClip;

    [Tooltip("Clip idle iniziale di Donna Due (fallback).")]
    public AnimationClip donnaDueIdlePreClip;

    [Tooltip("Clip idle finale di Donna Due (preferito dopo il movimento).")]
    public AnimationClip donnaDueIdlePostClip;

    [Header("Scene 6 - Fiammella Statica")]
    [Tooltip("Se true, aggiunge una fiammella statica sopra le lucerne di Scena6.")]
    public bool enableScene6StaticTorchFlames = true;

    [Tooltip("Nome scena su cui applicare la fiammella statica.")]
    public string scene6TorchSceneName = "Scena6";

    [Tooltip("Nome dei GO target a cui agganciare l'effetto.")]
    public string scene6TorchObjectName = "Mausoleo_Lucerna";

    [Tooltip("Nome del figlio runtime creato sotto ogni lucerna.")]
    public string scene6TorchEffectRootName = "_StaticTorchGlow";

    [Tooltip("Texture in Resources usata per il glow statico della fiammella.")]
    public string scene6TorchGlowTextureResourceName = "StaticTorchGlow";

    [Min(0f)]
    [Tooltip("Offset verticale in metri sopra al bounds della lucerna.")]
    public float scene6TorchHeightAboveBounds = 0.035f;

    [Tooltip("Correzione verticale additiva della fiammella. Valori negativi la abbassano verso la lucerna.")]
    public float scene6TorchVerticalOffsetAdjustment = -0.03f;

    [Tooltip("Tinta principale della fiammella statica.")]
    public Color scene6TorchGlowColor = new Color(1f, 0.72f, 0.34f, 0.92f);

    [Tooltip("Tinta piu' intensa del cuore della fiammella.")]
    public Color scene6TorchInnerGlowColor = new Color(1f, 0.93f, 0.72f, 0.85f);

    [Tooltip("Colore della luce emessa.")]
    public Color scene6TorchLightColor = new Color(1f, 0.74f, 0.42f, 1f);

    [Min(0.01f)] public float scene6TorchGlowWidth = 0.075f;
    [Min(0.01f)] public float scene6TorchGlowHeight = 0.13f;
    [Min(0.01f)] public float scene6TorchInnerGlowScale = 0.56f;
    [Range(0f, 8f)] public float scene6TorchLightIntensity = 1.35f;
    [Range(0.1f, 8f)] public float scene6TorchLightRange = 1.8f;

    [Header("Scene 8 - Chiesa Delayed Fade")]
    [Tooltip("Se true, in Scena8 la chiesa resta spenta all'ingresso e compare con fade dopo un delay.")]
    public bool enableScene8ChurchDelayedFade = true;

    [Tooltip("Nome scena su cui applicare il fade ritardato della chiesa.")]
    public string scene8Name = "Scena8";

    [Tooltip("Nome del GO target della chiesa da far comparire con fade.")]
    public string scene8ChurchObjectName = "Chiesa_Tot";

    [Min(0f)]
    [Tooltip("Ritardo prima dell'accensione della chiesa in Scena8.")]
    public float scene8ChurchDelaySeconds = 12f;

    [Min(0.01f)]
    [Tooltip("Durata del fade-in della chiesa in Scena8.")]
    public float scene8ChurchFadeDuration = 2f;

    [Header("Scene 3 - Epigrafe Entrance")]
    [Tooltip("Se true, in Scena3 l'Epigrafe fa un piccolo movimento verso il player e uno scale-up graduale.")]
    public bool animateEpigrafeOnScene3 = true;

    [Tooltip("Nome scena target per l'animazione Epigrafe.")]
    public string scene3Name = "Scena3";

    [Tooltip("Token da cercare nel nome del GO Epigrafe (match case-insensitive).")]
    public string epigrafeNameToken = "Epigrafe";

    [Tooltip("Quota del tragitto verso il player da percorrere (0.5 = punto medio tra partenza e player).")]
    [Range(0f, 1f)] public float epigrafeMoveTowardsPlayerFraction = 0.5f;

    [Tooltip("Fallback: quanto si muove verso il player (metri) se il player non e' disponibile.")]
    [Min(0f)] public float epigrafeMoveTowardsPlayerDistance = 0.22f;

    [Tooltip("Moltiplicatore di scala finale (2 = circa il doppio).")]
    [Min(0.1f)] public float epigrafeScaleMultiplier = 3f;

    [Tooltip("Durata animazione in secondi.")]
    [Min(0.01f)] public float epigrafeEntranceDuration = 2.2f;
    [Tooltip("Delay prima della partenza dell'animazione in Scena3.")]
    [Min(0f)] public float epigrafeEntranceDelay = 5f;

    [Header("Scene Spawn Dissolve FX")]
    [Tooltip("Se true, all'attivazione di una scena virtuale applica un dissolve URP temporaneo ai renderer della scena.")]
    public bool enableSceneSpawnDissolveFx = true;
    public bool disableSceneSpawnDissolveOnMobile = true;

    [Range(1, 256)]
    [Tooltip("Numero massimo di renderer coinvolti nel dissolve per ogni cambio scena.")]
    public int sceneSpawnDissolveMaxRenderers = 96;

    [Range(0.15f, 6f)]
    [Tooltip("Durata del dissolve da smaterializzato a materializzato.")]
    public float sceneSpawnDissolveDuration = 3f;

    [Range(0f, 0.25f)]
    [Tooltip("Piccolo sfasamento tra i gruppi principali della scena.")]
    public float sceneSpawnDissolveGroupStagger = 0.03f;

    [Min(0.01f)]
    [Tooltip("Ignora renderer troppo piccoli per evitare rumore visivo.")]
    public float sceneSpawnDissolveMinBoundsSize = 0.15f;

    [Range(0.001f, 0.25f)]
    [Tooltip("Spessore del bordo luminoso del dissolve.")]
    public float sceneSpawnDissolveEdgeWidth = 0.055f;

    [Range(0f, 8f)]
    [Tooltip("Intensita' del bordo luminoso del dissolve.")]
    public float sceneSpawnDissolveEdgeIntensity = 1.15f;

    [Tooltip("Colore del bordo del dissolve.")]
    public Color sceneSpawnDissolveEdgeColor = new Color(0.62f, 0.94f, 1f, 1f);

    [Range(0.1f, 128f)]
    [Tooltip("Scala del rumore usato dal dissolve.")]
    public float sceneSpawnDissolveNoiseScale = 24f;

    [Header("Editor Hotkey")]
    [Tooltip("In Play Mode da Editor, premi H per passare subito alla scena successiva.")]
    public bool enableEditorNextSceneHotkey = true;
    [Range(0.05f, 1.0f)]
    public float editorNextSceneHotkeyCooldown = 0.2f;

    [Header("Scene 1 - Player Pose Lock")]
    [Tooltip("Se true, in Scena1 forza la posa del player davanti al plastico anche dopo l'inizializzazione XR.")]
    public bool forceScene1PlayerPose = true;

    [Tooltip("Per quanti frame riafferma la posa in Scena1 (utile in build XR).")]
    [Range(0, 120)]
    public int scene1PoseReapplyFrames = 45;

    [Tooltip("Finestra temporale massima per riaffermare la posa in Scena1.")]
    [Range(0f, 3f)]
    public float scene1PoseReapplySeconds = 1.2f;

    [Tooltip("Se true, in Scena1 orienta il player verso PlasticoMappa.")]
    public bool scene1FacePlastico = true;

    [Header("Scene 1 - Plastico Height")]
    [Tooltip("Se true, in Scena1 alza il plastico della mappa rispetto alla posizione base.")]
    public bool adjustScene1PlasticoHeight = true;

    [Min(0f)]
    [Tooltip("Offset verticale in metri applicato al PlasticoMappa solo in Scena1.")]
    public float scene1PlasticoHeightOffset = 0.30f;

    [Header("Startup View Blocker")]
    [Tooltip("Se true, all'avvio nasconde per un attimo la vista mentre la posa della Scena1 viene stabilizzata.")]
    public bool hideStartupScene1PoseAdjustments = true;
    [Tooltip("Se true, riusa il blocker anche ogni volta che si rientra in Scena1 dal menu.")]
    public bool hideScene1PoseAdjustmentsOnEveryEntry = true;

    [Tooltip("Margine extra oltre al tempo di reapply della posa, prima di togliere il nero iniziale.")]
    [Range(0f, 0.5f)]
    public float startupScene1BlockExtraSeconds = 0.08f;

    [Tooltip("Fade-out del nero iniziale dopo il fix della posa.")]
    [Range(0.01f, 0.5f)]
    public float startupScene1UnblockFadeSeconds = 0.12f;

    [Header("Language Selection Smooth Start")]
    [Range(16, 2048)]
    [Tooltip("Numero massimo di renderer/collider ripristinati per frame dopo la scelta lingua.")]
    public int languageSelectionRestoreComponentsPerFrame = 128;

    [Range(0.25f, 8f)]
    [Tooltip("Budget massimo in millisecondi usato ogni frame per ripristinare oggetti nascosti dopo la scelta lingua.")]
    public float languageSelectionRestoreFrameBudgetMs = 2f;

    [Range(0f, 0.3f)]
    [Tooltip("Pausa minima del blocker nero dopo il ripristino, prima del fade-out.")]
    public float languageSelectionPostRestoreHoldSeconds = 0.03f;

    [Header("Language Selection Preload")]
    [Tooltip("Se true, durante la selezione lingua scalda scene, shader, materiali, mesh, audio e video prima dell'avvio esperienza.")]
    public bool preloadExperienceDuringLanguageSelection = false;

    [Tooltip("Se true, se l'utente sceglie una lingua mentre il preload e' in corso, l'avvio aspetta la fine del preload.")]
    public bool waitForLanguageSelectionPreloadBeforeStart = true;

    [Tooltip("Se true, il preload viene eseguito una sola volta per sessione app.")]
    public bool preloadExperienceOnlyOncePerSession = true;

    [Range(0, 8)]
    [Tooltip("Frame da lasciare alla UI lingua prima di iniziare il preload.")]
    public int preloadInitialDelayFrames = 2;

    [Range(0, 4)]
    [Tooltip("Frame di respiro tra il warmup di una scena virtuale e la successiva.")]
    public int preloadSettleFramesPerScene = 1;

    [Range(16, 256)]
    [Tooltip("Dimensione della RenderTexture usata dalla camera nascosta di warmup.")]
    public int preloadRenderTextureSize = 96;

    [Tooltip("Se true, esegue Shader.WarmupAllShaders durante la selezione lingua.")]
    public bool preloadWarmupShaders = false;

    [Tooltip("Se true, renderizza ogni scena una volta su RenderTexture nascosta per forzare upload/compilazione GPU prima dell'esperienza.")]
    public bool preloadRenderSceneOnce = false;

    [Tooltip("Se true, chiama Prepare sui VideoPlayer trovati durante il warmup.")]
    public bool preloadPrepareVideos = false;

    private int _index = -1;
    private Coroutine _sequenceRoutine;
    private readonly List<Coroutine> _donnaRoutines = new List<Coroutine>(4);
    private Coroutine _epigrafeEntranceRoutine;
    private Coroutine _scene1PoseRoutine;
    private Coroutine _scene8ChurchFadeRoutine;
    private Coroutine _scene7DelayedDissolveRoutine;
    private readonly Dictionary<int, PlayableGraph> _donnaGraphs = new Dictionary<int, PlayableGraph>(4);
    private bool _isScene1RuleActive;
    private float _lastEditorHotkeyTime;
    private readonly Dictionary<VideoPlayer, RenderTexture> _runtimeVideoTextures = new Dictionary<VideoPlayer, RenderTexture>();
    private readonly Dictionary<VideoPlayer, Vector3> _videoPlaneBaseScales = new Dictionary<VideoPlayer, Vector3>();
    private readonly Dictionary<Transform, Vector3> _epigrafeBaseLocalPositions = new Dictionary<Transform, Vector3>();
    private readonly Dictionary<Transform, Vector3> _epigrafeBaseLocalScales = new Dictionary<Transform, Vector3>();
    private readonly Dictionary<Transform, Vector3> _scene1PlasticoBaseLocalPositions = new Dictionary<Transform, Vector3>();
    private readonly Dictionary<int, CameraClearFlags> _cameraClearFlagsBackup = new Dictionary<int, CameraClearFlags>(8);
    private readonly Dictionary<int, Color> _cameraBackgroundBackup = new Dictionary<int, Color>(8);
    private readonly Dictionary<string, List<GameObject>> _sceneObjectsByExactNameCache = new Dictionary<string, List<GameObject>>(8);
    private readonly List<Scene8ChurchTargetState> _scene8ChurchTargetStates = new List<Scene8ChurchTargetState>(4);
    private readonly List<Scene8RendererFadeState> _scene8ChurchRendererStates = new List<Scene8RendererFadeState>(32);
    private readonly List<Scene8LightFadeState> _scene8ChurchLightStates = new List<Scene8LightFadeState>(8);
    private Material _initialSkyboxMaterial;
    private Material _runtimeSkyboxMaterial;
    private Texture _runtimeSkyTexture;
    private Material _runtimeSkyDomeMaterial;
    private Material _runtimeScene6TorchFlameMaterial;
    private Transform _runtimeSkyDomeTransform;
    private MeshRenderer _runtimeSkyDomeRenderer;
    private Mesh _runtimeSkyDomeMesh;
    private bool _hasCapturedInitialSkybox;
    private bool _hasWarnedMissingSkyboxTexture;
    private bool _hasWarnedMissingSkyDomeShader;
    private bool _hasWarnedMissingScene6TorchTexture;
    private bool _hasWarnedMissingScene6TorchShader;
    private bool _hasShownStartupScene1ViewBlocker;
    private Coroutine _startupViewBlockerRoutine;
    private Coroutine _languagePreferenceSaveRoutine;
    private Coroutine _localizedRuntimeUiRefreshRoutine;
    private Coroutine _languageSelectionCompletionRoutine;
    private Coroutine _languageSelectionPreloadRoutine;
    private Coroutine _queuedLanguageSelectionRoutine;
    private Transform _startupViewBlockerTransform;
    private CanvasGroup _startupViewBlockerCanvasGroup;
    private SceneActivationUrpDissolveVfx _sceneSpawnDissolveVfx;
    private Camera[] _cachedSceneCameras = Array.Empty<Camera>();
    private float _nextContinuousSceneRuleRefreshTime = -1f;
    private float _nextCameraCacheRefreshTime = -1f;
    private GameObject _scene7DelayedDissolveTarget;
    private bool _scene7DelayedDissolveTargetOriginalActive;
    private GameObject _languageSelectionRoot;
    private Transform _languageSelectionTransform;
    private bool _waitingForLanguageSelection;
    private bool _restartLanguageSelectionOnResume;
    private bool _suppressScene1ViewBlocker;
    private bool _suppressNextSceneSpawnDissolve;
    private bool _languageSelectionPreloadRunning;
    private bool _languageSelectionPreloadComplete;
    private bool _languageSelectionPreloadEverCompleted;
    private bool _languageSelectionInputLockedForPreload;
    private bool _hasQueuedLanguageAfterPreload;
    private bool _languageSelectionFixedInWorld;
    private float _lastLanguageSelectionFallbackPressTime = -10f;
    private bool _previousLanguageSelectionFallbackPressed;
    private NureLanguageSelectionButton _focusedLanguageButton;
    private NureLanguageSelectionButton _italianLanguageButton;
    private NureLanguageSelectionButton _englishLanguageButton;
    private NureLanguage _queuedLanguageAfterPreload;
    private Material _italianLanguageMaterial;
    private Material _englishLanguageMaterial;
    private Texture2D _italianLanguageTexture;
    private Texture2D _englishLanguageTexture;
    private Mesh _languageFlagPlaneMesh;
    private readonly List<LanguageRendererState> _languageHiddenRenderers = new List<LanguageRendererState>(256);
    private readonly List<LanguageColliderState> _languageHiddenColliders = new List<LanguageColliderState>(256);

    private sealed class Scene8ChurchTargetState
    {
        public GameObject Target;
        public bool OriginalActive;
    }

    private sealed class LanguageRendererState
    {
        public Renderer Renderer;
        public bool Enabled;
    }

    private sealed class LanguageColliderState
    {
        public Collider Collider;
        public bool Enabled;
    }

    private struct PreloadAudioSourceState
    {
        public AudioSource Source;
        public bool PlayOnAwake;
        public bool Mute;
        public float Volume;
    }

    private struct PreloadVideoPlayerState
    {
        public VideoPlayer Player;
        public bool PlayOnAwake;
    }

    private sealed class Scene8RendererFadeState
    {
        public Renderer Renderer;
        public Material[] OriginalSharedMaterials;
        public Material[] RuntimeMaterials;
    }

    private sealed class Scene8LightFadeState
    {
        public Light Light;
        public float OriginalIntensity;
    }

    private void Awake()
    {
        if (Application.isMobilePlatform && !Debug.isDebugBuild)
        {
            preloadExperienceDuringLanguageSelection = false;
            preloadWarmupShaders = false;
            preloadRenderSceneOnce = false;
            preloadPrepareVideos = false;
        }

        // Hard lock richiesto: i GO target sono forzati dalla lista interna delle Rune da nascondere.
        scene1Name = ForcedScene1Name;
        globalObjectName = ForcedToggleObjectNames[0];
        enforceScene1PlasticoOnly = false;

        if (!Application.isEditor && !Debug.isDebugBuild)
            verboseLogs = false;

        if (!_hasCapturedInitialSkybox)
        {
            _initialSkyboxMaterial = RenderSettings.skybox;
            _hasCapturedInitialSkybox = true;
        }

        // Auto-find audio source se non assegnato (consiglio comunque di assegnarlo in Inspector)
        if (audioSource == null)
        {
            audioSource = GetComponent<AudioSource>();
            if (audioSource == null)
                audioSource = FindFirstObjectByType<AudioSource>();
        }

        EnsureDefaultLocalizedSceneContent();

        if (enableLocalization && showLanguageSelectionOnStart)
            currentLanguage = NureLanguage.Italian;
        else if (enableLocalization && rememberLanguagePreference && PlayerPrefs.HasKey("NURE.Language"))
        {
            string saved = PlayerPrefs.GetString("NURE.Language", currentLanguage.ToString());
            if (Enum.TryParse(saved, out NureLanguage parsed))
                currentLanguage = parsed;
        }
    }

    private void Start()
    {
        if (enableLocalization && showLanguageSelectionOnStart)
        {
            PrepareLanguageSelectionBackdrop();
            ShowLanguageSelection();
            return;
        }

        if (playSequenceOnStart)
        {
            DeactivateAllScenes();
            PlayFromStart();
        }
        else
        {
            SyncToCurrentlyActive();
        }

        if (enforceSkyboxByScene)
            ApplySkyboxRuleForCurrentActiveScene();
    }

    private void Update()
    {
        UpdateLanguageSelectionFallbackInput();

#if UNITY_EDITOR
        HandleEditorNextSceneHotkey();
#endif
    }

    private void LateUpdate()
    {
        UpdateLanguageSelectionPose();

        if (IsRuntimeSkyDomeVisible())
            UpdateRuntimeSkyDomePose();

        if (Time.unscaledTime < _nextContinuousSceneRuleRefreshTime)
            return;

        _nextContinuousSceneRuleRefreshTime = Time.unscaledTime + ContinuousSceneRuleRefreshInterval;

        if (enforceGlobalObjectByScene && enforceGlobalObjectContinuously)
        {
            bool shouldForceOff = IsRuneOffRuleCurrentlyActive();
            _isScene1RuleActive = shouldForceOff;
            ApplyGlobalObjectRule(shouldForceOff);
        }

        if (enforceSkyboxByScene && enforceSkyboxContinuously)
            ApplySkyboxRuleForCurrentActiveScene();
    }

#if UNITY_EDITOR
    private void HandleEditorNextSceneHotkey()
    {
        if (!enableEditorNextSceneHotkey || !Application.isPlaying)
            return;

        if (Time.unscaledTime - _lastEditorHotkeyTime < editorNextSceneHotkeyCooldown)
            return;

        if (!IsEditorNextSceneKeyPressed())
            return;

        _lastEditorHotkeyTime = Time.unscaledTime;
        ActivateNextSceneImmediate();
    }

    private bool IsEditorNextSceneKeyPressed()
    {
#if ENABLE_LEGACY_INPUT_MANAGER
        if (Input.GetKeyDown(KeyCode.H))
            return true;
#endif

#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null && Keyboard.current.hKey.wasPressedThisFrame)
            return true;
#endif

        return false;
    }

    private void ActivateNextSceneImmediate()
    {
        if (_sequenceRoutine != null)
        {
            Next();
            if (verboseLogs) Debug.Log("[SceneGroupManager] Hotkey H -> Next() su sequenza attiva.");
            return;
        }

        if (scenes == null || scenes.Count == 0)
            return;

        int current = FindActiveSceneIndex();
        int next = current < 0 ? 0 : (current + 1) % scenes.Count;

        VirtualScene cfg = scenes[next];
        if (cfg == null || cfg.root == null)
            return;

        _index = next;
        ActivateScene(cfg.root);

        if (verboseLogs) Debug.Log($"[SceneGroupManager] Hotkey H -> scena successiva: {cfg.root.name} (index {next})");
    }
#endif

    private void OnDisable()
    {
        StopScene7DelayedDissolve(restoreTarget: true);

        if (_sceneSpawnDissolveVfx != null)
            _sceneSpawnDissolveVfx.StopAndClear();

        StopScene8ChurchDelayedFade(restoreState: true);
        StopScene1PoseRoutine();
        StopAndDestroyStartupViewBlocker();
        StopLanguageSelectionCompletion();
        StopLanguageSelectionPreload();
        DestroyLanguageSelection();
        StopEpigrafeEntrance();
        StopDonnaSequence();
        ReleaseRuntimeVideoTextures();
        DestroyRuntimeSkyDome();
        ReleaseScene6TorchFlameMaterial();
        RestoreSkyboxAndCameraDefaults();
    }

    private void OnApplicationPause(bool pauseStatus)
    {
        if (!enableLocalization || !resetToLanguageSelectionOnPause)
            return;

        if (pauseStatus)
        {
            _restartLanguageSelectionOnResume = true;
            ResetToLanguageSelection();
        }
        else if (_restartLanguageSelectionOnResume)
        {
            _restartLanguageSelectionOnResume = false;
            ShowLanguageSelection();
        }
    }

    private void OnApplicationFocus(bool hasFocus)
    {
        if (!enableLocalization || !resetToLanguageSelectionOnPause)
            return;

        if (!hasFocus)
        {
            _restartLanguageSelectionOnResume = true;
            ResetToLanguageSelection();
        }
    }

    public void SelectLanguageItalian()
    {
        SelectLanguage(NureLanguage.Italian);
    }

    public void SelectLanguageEnglish()
    {
        SelectLanguage(NureLanguage.English);
    }

    public void SelectLanguage(NureLanguage language)
    {
        if (QueueLanguageSelectionUntilPreloadCompletes(language))
            return;

        CompleteLanguageSelection(language);
    }

    private void CompleteLanguageSelection(NureLanguage language)
    {
        if (_languageSelectionCompletionRoutine != null)
            return;

        _languageSelectionCompletionRoutine = StartCoroutine(CompleteLanguageSelectionRoutine(language));
    }

    private IEnumerator CompleteLanguageSelectionRoutine(NureLanguage language)
    {
        _languageSelectionInputLockedForPreload = true;

        currentLanguage = language;
        LanguageVersion++;
        LanguageChanged?.Invoke();

        if (rememberLanguagePreference)
        {
            PlayerPrefs.SetString("NURE.Language", currentLanguage.ToString());
            QueueLanguagePreferenceSave();
        }

        bool transitionBlockerVisible = ShowLanguageSelectionTransitionBlocker();

        DestroyLanguageSelection();
        yield return null;

        ScheduleLocalizedRuntimeUIRefresh();

        if (activateFirstSceneAfterLanguageSelection && TryGetFirstConfiguredScene(out VirtualScene firstScene))
        {
            yield return ActivateFirstSceneAfterLanguageSelectionRoutine(firstScene);
        }
        else
        {
            yield return RestoreObjectsHiddenForLanguageSelectionRoutine();

            if (playSequenceOnStart)
                PlayFromStart();
        }

        if (transitionBlockerVisible)
            FadeLanguageSelectionTransitionBlocker();

        _languageSelectionCompletionRoutine = null;
    }

    private bool QueueLanguageSelectionUntilPreloadCompletes(NureLanguage language)
    {
        if (!waitForLanguageSelectionPreloadBeforeStart || !_languageSelectionPreloadRunning || _languageSelectionPreloadComplete)
            return false;

        _queuedLanguageAfterPreload = language;
        _hasQueuedLanguageAfterPreload = true;
        _languageSelectionInputLockedForPreload = true;

        if (_queuedLanguageSelectionRoutine == null)
            _queuedLanguageSelectionRoutine = StartCoroutine(CompleteQueuedLanguageSelectionAfterPreload());

        if (verboseLogs)
            Debug.Log($"[SceneGroupManager] Language selection queued until preload completes: {language}");

        return true;
    }

    private IEnumerator CompleteQueuedLanguageSelectionAfterPreload()
    {
        while (_languageSelectionPreloadRunning)
            yield return null;

        _queuedLanguageSelectionRoutine = null;
        _languageSelectionInputLockedForPreload = false;

        if (!_hasQueuedLanguageAfterPreload || !_waitingForLanguageSelection)
        {
            _hasQueuedLanguageAfterPreload = false;
            yield break;
        }

        NureLanguage queuedLanguage = _queuedLanguageAfterPreload;
        _hasQueuedLanguageAfterPreload = false;
        CompleteLanguageSelection(queuedLanguage);
    }

    private bool TryGetFirstConfiguredScene(out VirtualScene cfg)
    {
        cfg = null;

        if (scenes == null || scenes.Count == 0)
            return false;

        cfg = scenes[0];
        return cfg != null && cfg.root != null;
    }

    private IEnumerator ActivateFirstSceneAfterLanguageSelectionRoutine(VirtualScene cfg)
    {
        if (cfg == null || cfg.root == null)
            yield break;

        _index = 0;
        if (disableInitialSceneDissolveAfterLanguageSelection)
            _suppressNextSceneSpawnDissolve = true;

        bool previousSuppressScene1ViewBlocker = _suppressScene1ViewBlocker;
        _suppressScene1ViewBlocker = true;

        if (!cfg.root.activeSelf)
        {
            cfg.root.SetActive(true);
            yield return null;
        }

        ActivateOnly(cfg);
        yield return null;

        yield return RestoreObjectsHiddenForLanguageSelectionRoutine();

        yield return ApplySpawnAudioVideoRoutine(cfg);

        _suppressScene1ViewBlocker = previousSuppressScene1ViewBlocker;
    }

    private bool ShowLanguageSelectionTransitionBlocker()
    {
        if (!EnsureStartupViewBlockerCanvas())
            return false;

        if (_startupViewBlockerRoutine != null)
        {
            StopCoroutine(_startupViewBlockerRoutine);
            _startupViewBlockerRoutine = null;
        }

        _startupViewBlockerCanvasGroup.alpha = 1f;
        return true;
    }

    private void FadeLanguageSelectionTransitionBlocker()
    {
        if (_startupViewBlockerCanvasGroup == null)
            return;

        if (_startupViewBlockerRoutine != null)
            StopCoroutine(_startupViewBlockerRoutine);

        _startupViewBlockerRoutine = StartCoroutine(StartupViewBlockerRoutine(
            Mathf.Max(0f, languageSelectionPostRestoreHoldSeconds)));
    }

    private void StopLanguageSelectionCompletion()
    {
        if (_languageSelectionCompletionRoutine == null)
            return;

        StopCoroutine(_languageSelectionCompletionRoutine);
        _languageSelectionCompletionRoutine = null;
        _suppressScene1ViewBlocker = false;
    }

    private void QueueLanguagePreferenceSave()
    {
        if (_languagePreferenceSaveRoutine != null)
            StopCoroutine(_languagePreferenceSaveRoutine);

        _languagePreferenceSaveRoutine = StartCoroutine(SaveLanguagePreferenceDeferred());
    }

    private IEnumerator SaveLanguagePreferenceDeferred()
    {
        yield return null;
        yield return new WaitForSecondsRealtime(2f);
        PlayerPrefs.Save();
        _languagePreferenceSaveRoutine = null;
    }

    public string GetLocalizedSceneLabel(GameObject sceneRootOrChild)
    {
        VirtualScene cfg = FindConfigForObject(sceneRootOrChild);
        return GetLocalizedSceneLabel(cfg);
    }

    public string GetLocalizedSceneLabel(VirtualScene cfg)
    {
        if (cfg == null)
            return string.Empty;

        LocalizedSceneContent content = FindLocalizedSceneContent(cfg);
        if (content != null)
        {
            string localized = currentLanguage == NureLanguage.English ? content.englishLabel : content.italianLabel;
            if (!string.IsNullOrWhiteSpace(localized))
                return localized;
        }

        if (!string.IsNullOrWhiteSpace(cfg.name))
            return cfg.name;

        return cfg.root != null ? cfg.root.name : string.Empty;
    }

    public string GetLocalizedInterfaceText(string key, string fallback)
    {
        if (currentLanguage == NureLanguage.Italian)
            return fallback;

        switch ((key ?? string.Empty).Trim().ToLowerInvariant())
        {
            case "menu":
                return "MENU";
            case "move":
                return "MOVE";
            case "choose_language_title":
                return "Choose your language";
            case "choose_language_subtitle":
                return "Select a language to start the experience";
            case "italian":
                return "Italian";
            case "english":
                return "English";
            default:
                return fallback;
        }
    }

    private void ResetToLanguageSelection()
    {
        StopSequence();

        if (audioSource != null)
            audioSource.Stop();

        VirtualScene cfg = GetCurrentCfg();
        if (cfg != null && cfg.videoPlayer != null)
        {
            try { cfg.videoPlayer.Stop(); } catch { }
        }

        PrepareLanguageSelectionBackdrop();
        currentLanguage = NureLanguage.Italian;
        LanguageVersion++;
        LanguageChanged?.Invoke();
        ShowLanguageSelection();
    }

    private AudioClip ResolveAudioClip(VirtualScene cfg)
    {
        if (!enableLocalization || cfg == null)
            return cfg != null ? cfg.audioClip : null;

        LocalizedSceneContent content = FindLocalizedSceneContent(cfg);
        if (content != null)
        {
            AudioClip localized = currentLanguage == NureLanguage.English ? content.englishAudioClip : content.italianAudioClip;
            if (localized != null)
                return localized;
        }

        return cfg.audioClip;
    }

    private LocalizedSceneContent FindLocalizedSceneContent(VirtualScene cfg)
    {
        if (cfg == null || localizedScenes == null)
            return null;

        string cfgName = !string.IsNullOrWhiteSpace(cfg.name) ? cfg.name : (cfg.root != null ? cfg.root.name : string.Empty);
        for (int i = 0; i < localizedScenes.Count; i++)
        {
            LocalizedSceneContent content = localizedScenes[i];
            if (content == null || string.IsNullOrWhiteSpace(content.sceneName))
                continue;

            if (string.Equals(content.sceneName, cfgName, StringComparison.OrdinalIgnoreCase))
                return content;
        }

        if (cfg.root != null)
        {
            for (int i = 0; i < localizedScenes.Count; i++)
            {
                LocalizedSceneContent content = localizedScenes[i];
                if (content == null || string.IsNullOrWhiteSpace(content.sceneName))
                    continue;

                if (string.Equals(content.sceneName, cfg.root.name, StringComparison.OrdinalIgnoreCase))
                    return content;
            }
        }

        return null;
    }

    private void EnsureDefaultLocalizedSceneContent()
    {
        if (localizedScenes == null)
            localizedScenes = new List<LocalizedSceneContent>();

        string[] italian =
        {
            "MAPPA SITO ARCHEOLOGICO",
            "CHIESA PALEOCRISTIANA e TOMBE",
            "EPIGRAFE AMOCADA",
            "NURAGHE E VILLAGGIO",
            "TOMBE A FOSSA",
            "MAUSOLEO E REFRIGERIUM",
            "TOMBE A CAMERA",
            "TOMBE A FOSSA E SARCOFAGO",
            "INTERNO CHIESA PALEOCRISTIANA",
            "SANTA LUCIA",
        };

        string[] english =
        {
            "ARCHAEOLOGICAL SITE MAP",
            "EARLY CHRISTIAN CHURCH AND GRAVES",
            "AMOCADA INSCRIPTION",
            "NURAGHE AND VILLAGE",
            "PIT GRAVES",
            "MAUSOLEUM AND REFRIGERIUM",
            "CHAMBER TOMBS",
            "PIT GRAVES AND SARCOPHAGUS",
            "INSIDE THE EARLY CHRISTIAN CHURCH",
            "SAINT LUCIA",
        };

        int count = Mathf.Min(scenes != null ? scenes.Count : 0, italian.Length);
        for (int i = 0; i < count; i++)
        {
            VirtualScene cfg = scenes[i];
            if (cfg == null)
                continue;

            string sceneName = !string.IsNullOrWhiteSpace(cfg.name) ? cfg.name : (cfg.root != null ? cfg.root.name : $"Scena{i + 1}");
            LocalizedSceneContent content = FindLocalizedSceneContent(cfg);
            if (content == null)
            {
                content = new LocalizedSceneContent { sceneName = sceneName };
                localizedScenes.Add(content);
            }

            if (string.IsNullOrWhiteSpace(content.italianLabel))
                content.italianLabel = italian[i];
            if (string.IsNullOrWhiteSpace(content.englishLabel))
                content.englishLabel = english[i];
            if (content.italianAudioClip == null)
                content.italianAudioClip = cfg.audioClip;
        }
    }

    private void ShowLanguageSelection()
    {
        StopLanguageSelectionCompletion();
        currentLanguage = NureLanguage.Italian;
        _previousLanguageSelectionFallbackPressed = false;
        _lastLanguageSelectionFallbackPressTime = -10f;

        if (rememberLanguagePreference)
            PlayerPrefs.DeleteKey("NURE.Language");

        DestroyLanguageSelection();
        PrepareLanguageSelectionBackdrop();
        _waitingForLanguageSelection = true;
        BuildLanguageSelectionUI();
        HideObjectsForLanguageSelection();
        RefreshLocalizedRuntimeUI();
        StartLanguageSelectionPreload();
    }

    private void PrepareLanguageSelectionBackdrop()
    {
        StopSequence();

        if (audioSource != null)
            audioSource.Stop();

        DeactivateAllScenes();
        StopAndDestroyStartupViewBlocker();

        if (scenes == null || scenes.Count == 0)
            return;

        VirtualScene cfg = scenes[0];
        if (cfg == null)
            return;

        _index = 0;

        if (player != null && cfg.playerSpawn != null)
        {
            _suppressScene1ViewBlocker = true;
            ApplyPlayerPoseFromSpawn(cfg, cfg.playerSpawn);
            _suppressScene1ViewBlocker = false;
        }

        StopAndDestroyStartupViewBlocker();

        if (!ShouldActivateFirstSceneDuringLanguageSelection(cfg))
            return;

        if (!cfg.root.activeSelf)
            cfg.root.SetActive(true);

        ApplySceneSpecificVisibility(cfg);

        if (enforceSkyboxByScene)
            ApplySkyboxRuleForCurrentActiveScene();
    }

    private bool ShouldActivateFirstSceneDuringLanguageSelection(VirtualScene cfg)
    {
        if (cfg == null || cfg.root == null)
            return false;

        return showFirstSceneBehindLanguageSelection
            || (preactivateFirstSceneDuringLanguageSelection && activateFirstSceneAfterLanguageSelection);
    }

    private void DestroyLanguageSelection()
    {
        _waitingForLanguageSelection = false;
        _languageSelectionFixedInWorld = false;
        _languageSelectionInputLockedForPreload = false;
        _hasQueuedLanguageAfterPreload = false;
        if (_languageSelectionRoot != null)
        {
            _languageSelectionRoot.SetActive(false);
            Destroy(_languageSelectionRoot);
        }

        ReleaseRuntimeLanguageAssets();

        _languageSelectionRoot = null;
        _languageSelectionTransform = null;
        _italianLanguageButton = null;
        _englishLanguageButton = null;
    }

    private void ReleaseRuntimeLanguageAssets()
    {
        if (_italianLanguageMaterial != null)
            Destroy(_italianLanguageMaterial);
        if (_englishLanguageMaterial != null)
            Destroy(_englishLanguageMaterial);
        if (_italianLanguageTexture != null)
            Destroy(_italianLanguageTexture);
        if (_englishLanguageTexture != null)
            Destroy(_englishLanguageTexture);
        if (_languageFlagPlaneMesh != null)
            Destroy(_languageFlagPlaneMesh);

        _italianLanguageMaterial = null;
        _englishLanguageMaterial = null;
        _italianLanguageTexture = null;
        _englishLanguageTexture = null;
        _languageFlagPlaneMesh = null;
    }

    private Texture ResolveLanguageFlagTexture(NureLanguage language)
    {
        Texture2D configuredTexture = language == NureLanguage.Italian
            ? italianLanguageFlagTexture
            : englishLanguageFlagTexture;

        if (configuredTexture != null)
            return configuredTexture;

        Texture2D runtimeTexture = CreateLanguageFlagTexture(language);
        if (language == NureLanguage.Italian)
            _italianLanguageTexture = runtimeTexture;
        else
            _englishLanguageTexture = runtimeTexture;

        return runtimeTexture;
    }

    private void HideObjectsForLanguageSelection()
    {
        RestoreObjectsHiddenForLanguageSelection();

        Renderer[] renderers = FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null || !renderer.enabled)
                continue;
            if (!renderer.gameObject.activeInHierarchy)
                continue;
            if (ShouldKeepVisibleDuringLanguageSelection(renderer.transform))
                continue;

            _languageHiddenRenderers.Add(new LanguageRendererState
            {
                Renderer = renderer,
                Enabled = renderer.enabled,
            });
            renderer.enabled = false;
        }

        Collider[] colliders = FindObjectsByType<Collider>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < colliders.Length; i++)
        {
            Collider collider = colliders[i];
            if (collider == null || !collider.enabled)
                continue;
            if (!collider.gameObject.activeInHierarchy)
                continue;
            if (ShouldKeepVisibleDuringLanguageSelection(collider.transform))
                continue;

            _languageHiddenColliders.Add(new LanguageColliderState
            {
                Collider = collider,
                Enabled = collider.enabled,
            });
            collider.enabled = false;
        }
    }

    private void RestoreObjectsHiddenForLanguageSelection()
    {
        for (int i = 0; i < _languageHiddenRenderers.Count; i++)
        {
            LanguageRendererState state = _languageHiddenRenderers[i];
            if (state != null && state.Renderer != null)
                state.Renderer.enabled = state.Enabled;
        }

        _languageHiddenRenderers.Clear();

        for (int i = 0; i < _languageHiddenColliders.Count; i++)
        {
            LanguageColliderState state = _languageHiddenColliders[i];
            if (state != null && state.Collider != null)
                state.Collider.enabled = state.Enabled;
        }

        _languageHiddenColliders.Clear();
    }

    private IEnumerator RestoreObjectsHiddenForLanguageSelectionRoutine()
    {
        int maxPerFrame = Mathf.Max(1, languageSelectionRestoreComponentsPerFrame);
        float frameBudgetSeconds = Mathf.Max(0.1f, languageSelectionRestoreFrameBudgetMs) * 0.001f;
        int restoredThisFrame = 0;
        float frameDeadline = Time.realtimeSinceStartup + frameBudgetSeconds;

        for (int i = 0; i < _languageHiddenRenderers.Count; i++)
        {
            LanguageRendererState state = _languageHiddenRenderers[i];
            if (state != null && state.Renderer != null)
                state.Renderer.enabled = state.Enabled;

            restoredThisFrame++;
            if (ShouldYieldLanguageRestoreWork(restoredThisFrame, maxPerFrame, frameDeadline))
            {
                restoredThisFrame = 0;
                yield return null;
                frameDeadline = Time.realtimeSinceStartup + frameBudgetSeconds;
            }
        }

        _languageHiddenRenderers.Clear();

        for (int i = 0; i < _languageHiddenColliders.Count; i++)
        {
            LanguageColliderState state = _languageHiddenColliders[i];
            if (state != null && state.Collider != null)
                state.Collider.enabled = state.Enabled;

            restoredThisFrame++;
            if (ShouldYieldLanguageRestoreWork(restoredThisFrame, maxPerFrame, frameDeadline))
            {
                restoredThisFrame = 0;
                yield return null;
                frameDeadline = Time.realtimeSinceStartup + frameBudgetSeconds;
            }
        }

        _languageHiddenColliders.Clear();
    }

    private static bool ShouldYieldLanguageRestoreWork(int restoredThisFrame, int maxPerFrame, float frameDeadline)
    {
        return restoredThisFrame >= maxPerFrame || Time.realtimeSinceStartup >= frameDeadline;
    }

    private void StartLanguageSelectionPreload()
    {
        if (!preloadExperienceDuringLanguageSelection)
        {
            _languageSelectionPreloadComplete = true;
            _languageSelectionPreloadRunning = false;
            return;
        }

        if (preloadExperienceOnlyOncePerSession && _languageSelectionPreloadEverCompleted)
        {
            _languageSelectionPreloadComplete = true;
            _languageSelectionPreloadRunning = false;
            return;
        }

        if (_languageSelectionPreloadRoutine != null)
            return;

        _languageSelectionPreloadComplete = false;
        _languageSelectionPreloadRunning = true;
        _languageSelectionPreloadRoutine = StartCoroutine(LanguageSelectionPreloadRoutine());
    }

    private void StopLanguageSelectionPreload()
    {
        if (_languageSelectionPreloadRoutine != null)
        {
            StopCoroutine(_languageSelectionPreloadRoutine);
            _languageSelectionPreloadRoutine = null;
        }

        if (_queuedLanguageSelectionRoutine != null)
        {
            StopCoroutine(_queuedLanguageSelectionRoutine);
            _queuedLanguageSelectionRoutine = null;
        }

        _languageSelectionPreloadRunning = false;
        _languageSelectionInputLockedForPreload = false;
        _hasQueuedLanguageAfterPreload = false;
    }

    private IEnumerator LanguageSelectionPreloadRoutine()
    {
        float startedAt = Time.realtimeSinceStartup;
        int previousAsyncUploadTimeSlice = QualitySettings.asyncUploadTimeSlice;
        int previousAsyncUploadBufferSize = QualitySettings.asyncUploadBufferSize;
        ThreadPriority previousBackgroundLoadingPriority = Application.backgroundLoadingPriority;

        try
        {
            QualitySettings.asyncUploadTimeSlice = Mathf.Max(previousAsyncUploadTimeSlice, 4);
            QualitySettings.asyncUploadBufferSize = Mathf.Max(previousAsyncUploadBufferSize, 64);
            Application.backgroundLoadingPriority = ThreadPriority.Low;

            int initialFrames = Mathf.Clamp(preloadInitialDelayFrames, 0, 8);
            for (int i = 0; i < initialFrames; i++)
                yield return null;

            WarmupRuntimeResourcesForPreload();
            yield return null;

            if (preloadWarmupShaders)
            {
                Shader.WarmupAllShaders();
                yield return null;
            }

            if (scenes != null)
            {
                for (int i = 0; i < scenes.Count; i++)
                    yield return PreloadVirtualSceneRoutine(scenes[i], i);
            }

            Canvas.ForceUpdateCanvases();

            _languageSelectionPreloadComplete = true;
            _languageSelectionPreloadEverCompleted = true;

            if (verboseLogs)
            {
                float elapsed = Time.realtimeSinceStartup - startedAt;
                Debug.Log($"[SceneGroupManager] Language selection preload completed in {elapsed:0.00}s.");
            }
        }
        finally
        {
            QualitySettings.asyncUploadTimeSlice = previousAsyncUploadTimeSlice;
            QualitySettings.asyncUploadBufferSize = previousAsyncUploadBufferSize;
            Application.backgroundLoadingPriority = previousBackgroundLoadingPriority;
            _languageSelectionPreloadRunning = false;
            _languageSelectionPreloadRoutine = null;
        }
    }

    private void WarmupRuntimeResourcesForPreload()
    {
        if (!string.IsNullOrWhiteSpace(globalSkyboxTextureResourceName))
            WarmupTextureForPreload(Resources.Load<Texture>(globalSkyboxTextureResourceName));

        if (!string.IsNullOrWhiteSpace(runtimeSkyDomeShaderResourceName))
            Resources.Load<Shader>(runtimeSkyDomeShaderResourceName);

        if (!string.IsNullOrWhiteSpace(scene6TorchGlowTextureResourceName))
            WarmupTextureForPreload(Resources.Load<Texture2D>(scene6TorchGlowTextureResourceName));

        WarmupMaterialForPreload(Resources.Load<Material>("SceneSpawnDissolveLitTemplate"));
        WarmupMaterialForPreload(Resources.Load<Material>("SceneSpawnDissolveUnlitTemplate"));
    }

    private IEnumerator PreloadVirtualSceneRoutine(VirtualScene cfg, int sceneIndex)
    {
        if (cfg == null)
            yield break;

        PreloadAudioForScene(cfg);

        if (cfg.root == null)
            yield break;

        bool rootWasActive = cfg.root.activeSelf;
        bool videoPanelWasActive = cfg.videoPanelRoot != null && cfg.videoPanelRoot.activeSelf;
        AudioSource[] audioSources = cfg.root.GetComponentsInChildren<AudioSource>(true);
        VideoPlayer[] videoPlayers = cfg.root.GetComponentsInChildren<VideoPlayer>(true);
        List<PreloadAudioSourceState> audioStates = CaptureAudioSourceStates(audioSources);
        List<PreloadVideoPlayerState> videoStates = CaptureVideoPlayerStates(videoPlayers);
        AddVideoPlayerStateIfMissing(videoStates, cfg.videoPlayer);

        try
        {
            PrepareAudioSourcesForSilentPreload(audioStates);
            PrepareVideoPlayersForSilentPreload(videoStates);

            if (cfg.videoPanelRoot != null && !cfg.videoPanelRoot.activeSelf)
                cfg.videoPanelRoot.SetActive(true);

            if (!cfg.root.activeSelf)
                cfg.root.SetActive(true);

            WarmupSceneForPreload(cfg.root);

            if (preloadPrepareVideos)
            {
                TryPrepareVideoForPreload(cfg.videoPlayer);
                for (int i = 0; i < videoPlayers.Length; i++)
                    TryPrepareVideoForPreload(videoPlayers[i]);
            }

            if (preloadRenderSceneOnce)
                RenderSceneForPreload(cfg.root);
        }
        finally
        {
            RestoreAudioSourcesAfterPreload(audioStates);
            RestoreVideoPlayersAfterPreload(videoStates);

            if (cfg.videoPanelRoot != null)
                cfg.videoPanelRoot.SetActive(videoPanelWasActive);

            cfg.root.SetActive(rootWasActive);
        }

        int settleFrames = Mathf.Clamp(preloadSettleFramesPerScene, 0, 4);
        for (int i = 0; i < settleFrames; i++)
            yield return null;

        if (verboseLogs)
        {
            string sceneName = string.IsNullOrWhiteSpace(cfg.name) ? cfg.root.name : cfg.name;
            Debug.Log($"[SceneGroupManager] Preloaded virtual scene {sceneIndex}: {sceneName}");
        }
    }

    private void PreloadAudioForScene(VirtualScene cfg)
    {
        if (cfg == null)
            return;

        PreloadAudioClipForPreload(cfg.audioClip);

        LocalizedSceneContent content = FindLocalizedSceneContent(cfg);
        if (content != null)
        {
            PreloadAudioClipForPreload(content.italianAudioClip);
            PreloadAudioClipForPreload(content.englishAudioClip);
        }
    }

    private static void PreloadAudioClipForPreload(AudioClip clip)
    {
        if (clip == null)
            return;

        if (clip.loadState == AudioDataLoadState.Unloaded)
            clip.LoadAudioData();
    }

    private static List<PreloadAudioSourceState> CaptureAudioSourceStates(AudioSource[] sources)
    {
        List<PreloadAudioSourceState> states = new List<PreloadAudioSourceState>(sources != null ? sources.Length : 0);
        if (sources == null)
            return states;

        for (int i = 0; i < sources.Length; i++)
        {
            AudioSource source = sources[i];
            if (source == null)
                continue;

            states.Add(new PreloadAudioSourceState
            {
                Source = source,
                PlayOnAwake = source.playOnAwake,
                Mute = source.mute,
                Volume = source.volume,
            });
        }

        return states;
    }

    private static List<PreloadVideoPlayerState> CaptureVideoPlayerStates(VideoPlayer[] players)
    {
        List<PreloadVideoPlayerState> states = new List<PreloadVideoPlayerState>(players != null ? players.Length : 0);
        if (players == null)
            return states;

        for (int i = 0; i < players.Length; i++)
            AddVideoPlayerStateIfMissing(states, players[i]);

        return states;
    }

    private static void AddVideoPlayerStateIfMissing(List<PreloadVideoPlayerState> states, VideoPlayer player)
    {
        if (states == null || player == null)
            return;

        for (int i = 0; i < states.Count; i++)
        {
            if (states[i].Player == player)
                return;
        }

        states.Add(new PreloadVideoPlayerState
        {
            Player = player,
            PlayOnAwake = player.playOnAwake,
        });
    }

    private static void PrepareAudioSourcesForSilentPreload(List<PreloadAudioSourceState> states)
    {
        if (states == null)
            return;

        for (int i = 0; i < states.Count; i++)
        {
            AudioSource source = states[i].Source;
            if (source == null)
                continue;

            source.playOnAwake = false;
            source.mute = true;
            source.Stop();
        }
    }

    private static void PrepareVideoPlayersForSilentPreload(List<PreloadVideoPlayerState> states)
    {
        if (states == null)
            return;

        for (int i = 0; i < states.Count; i++)
        {
            VideoPlayer player = states[i].Player;
            if (player == null)
                continue;

            player.playOnAwake = false;
        }
    }

    private static void RestoreAudioSourcesAfterPreload(List<PreloadAudioSourceState> states)
    {
        if (states == null)
            return;

        for (int i = 0; i < states.Count; i++)
        {
            PreloadAudioSourceState state = states[i];
            AudioSource source = state.Source;
            if (source == null)
                continue;

            source.Stop();
            source.playOnAwake = state.PlayOnAwake;
            source.mute = state.Mute;
            source.volume = state.Volume;
        }
    }

    private static void RestoreVideoPlayersAfterPreload(List<PreloadVideoPlayerState> states)
    {
        if (states == null)
            return;

        for (int i = 0; i < states.Count; i++)
        {
            PreloadVideoPlayerState state = states[i];
            VideoPlayer player = state.Player;
            if (player == null)
                continue;

            player.playOnAwake = state.PlayOnAwake;
        }
    }

    private void WarmupSceneForPreload(GameObject root)
    {
        if (root == null)
            return;

        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
            WarmupRendererForPreload(renderers[i]);

        Animator[] animators = root.GetComponentsInChildren<Animator>(true);
        for (int i = 0; i < animators.Length; i++)
        {
            Animator animator = animators[i];
            if (animator == null || !animator.isActiveAndEnabled)
                continue;

            try { animator.Update(0f); } catch { }
        }
    }

    private void WarmupRendererForPreload(Renderer renderer)
    {
        if (renderer == null)
            return;

        Mesh mesh = null;
        SkinnedMeshRenderer skinned = renderer as SkinnedMeshRenderer;
        if (skinned != null)
        {
            mesh = skinned.sharedMesh;
        }
        else
        {
            MeshFilter meshFilter = renderer.GetComponent<MeshFilter>();
            if (meshFilter != null)
                mesh = meshFilter.sharedMesh;
        }

        if (mesh != null)
        {
            try { mesh.UploadMeshData(false); } catch { }
        }

        Material[] materials = renderer.sharedMaterials;
        if (materials == null)
            return;

        for (int i = 0; i < materials.Length; i++)
            WarmupMaterialForPreload(materials[i]);
    }

    private void WarmupMaterialForPreload(Material material)
    {
        if (material == null)
            return;

        Shader shader = material.shader;
        if (shader != null && shader.isSupported)
        {
            int passCount = Mathf.Min(material.passCount, 4);
            for (int i = 0; i < passCount; i++)
            {
                try { material.SetPass(i); } catch { }
            }
        }

        string[] textureProperties = null;
        try { textureProperties = material.GetTexturePropertyNames(); } catch { }
        if (textureProperties == null)
            return;

        for (int i = 0; i < textureProperties.Length; i++)
            WarmupTextureForPreload(material.GetTexture(textureProperties[i]));
    }

    private static void WarmupTextureForPreload(Texture texture)
    {
        if (texture == null)
            return;

        int width = texture.width;
        int height = texture.height;
        if (width <= 0 || height <= 0)
            return;

        Texture2D texture2D = texture as Texture2D;
        if (texture2D != null && QualitySettings.streamingMipmapsActive)
            texture2D.requestedMipmapLevel = 0;

        RenderTexture renderTexture = texture as RenderTexture;
        if (renderTexture != null && !renderTexture.IsCreated())
            renderTexture.Create();
    }

    private void TryPrepareVideoForPreload(VideoPlayer videoPlayer)
    {
        if (videoPlayer == null)
            return;

        try
        {
            EnsureVideoOutputSetup(videoPlayer);
            if (!videoPlayer.isPrepared)
                videoPlayer.Prepare();
        }
        catch (Exception ex)
        {
            if (verboseLogs)
                Debug.LogWarning($"[SceneGroupManager] Video preload skipped for '{videoPlayer.name}': {ex.Message}");
        }
    }

    private void RenderSceneForPreload(GameObject root)
    {
        if (root == null || !TryGetRenderableBounds(root, out Bounds bounds))
            return;

        GameObject cameraGo = null;
        RenderTexture renderTexture = null;

        try
        {
            int textureSize = Mathf.Clamp(preloadRenderTextureSize, 16, 256);
            renderTexture = RenderTexture.GetTemporary(textureSize, textureSize, 16, RenderTextureFormat.ARGB32);

            cameraGo = new GameObject("RuntimePreloadCamera");
            cameraGo.hideFlags = HideFlags.HideAndDontSave;

            Camera warmupCamera = cameraGo.AddComponent<Camera>();
            warmupCamera.enabled = false;
            warmupCamera.stereoTargetEye = StereoTargetEyeMask.None;
            warmupCamera.clearFlags = CameraClearFlags.SolidColor;
            warmupCamera.backgroundColor = Color.black;
            warmupCamera.cullingMask = ~0;
            warmupCamera.orthographic = true;
            warmupCamera.allowHDR = false;
            warmupCamera.allowMSAA = false;
            warmupCamera.targetTexture = renderTexture;

            float maxExtent = Mathf.Max(bounds.extents.x, Mathf.Max(bounds.extents.y, bounds.extents.z));
            maxExtent = Mathf.Max(maxExtent, 0.5f);
            float distance = Mathf.Max(2f, maxExtent * 3f);

            warmupCamera.orthographicSize = maxExtent * 1.2f;
            warmupCamera.nearClipPlane = 0.01f;
            warmupCamera.farClipPlane = distance + (maxExtent * 4f) + 10f;
            warmupCamera.transform.SetPositionAndRotation(
                bounds.center + (Vector3.up * distance),
                Quaternion.LookRotation(Vector3.down, Vector3.forward));

            warmupCamera.Render();
        }
        catch (Exception ex)
        {
            if (verboseLogs)
                Debug.LogWarning($"[SceneGroupManager] Hidden scene render preload skipped for '{root.name}': {ex.Message}");
        }
        finally
        {
            if (renderTexture != null)
                RenderTexture.ReleaseTemporary(renderTexture);

            if (cameraGo != null)
                Destroy(cameraGo);
        }
    }

    private static bool TryGetRenderableBounds(GameObject root, out Bounds bounds)
    {
        bounds = new Bounds();
        if (root == null)
            return false;

        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        bool hasBounds = false;
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy)
                continue;

            if (!hasBounds)
            {
                bounds = renderer.bounds;
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(renderer.bounds);
            }
        }

        return hasBounds;
    }

    private bool ShouldKeepVisibleDuringLanguageSelection(Transform candidate)
    {
        if (candidate == null)
            return false;

        if (_languageSelectionTransform != null
            && (candidate == _languageSelectionTransform || candidate.IsChildOf(_languageSelectionTransform)))
        {
            return true;
        }

        if (player != null && (candidate == player || candidate.IsChildOf(player)))
            return true;

        if (candidate == transform || candidate.IsChildOf(transform))
            return true;

        return false;
    }

    private void BuildLanguageSelectionUI()
    {
        BuildLanguageSelection3D();
        return;

        #pragma warning disable CS0162
        Camera cam = ResolveMainCamera();

        _languageSelectionRoot = new GameObject("NureLanguageSelection");
        _languageSelectionTransform = _languageSelectionRoot.transform;
        if (cam != null)
            _languageSelectionTransform.SetParent(cam.transform, false);
        else
            DontDestroyOnLoad(_languageSelectionRoot);

        Canvas canvas = _languageSelectionRoot.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = cam;
        canvas.sortingOrder = 6000;

        CanvasScaler scaler = _languageSelectionRoot.AddComponent<CanvasScaler>();
        scaler.dynamicPixelsPerUnit = 12f;
        _languageSelectionRoot.AddComponent<GraphicRaycaster>();
        _languageSelectionRoot.AddComponent<TrackedDeviceGraphicRaycaster>();
        EnsureLanguageSelectionEventSystem();

        RectTransform rootRect = _languageSelectionRoot.GetComponent<RectTransform>();
        rootRect.sizeDelta = languagePanelSize;
        _languageSelectionTransform.localScale = Vector3.one * 0.001f;
        _languageSelectionTransform.localPosition = new Vector3(0f, -0.02f, languagePanelDistanceFromCamera);
        _languageSelectionTransform.localRotation = Quaternion.identity;

        Image panel = _languageSelectionRoot.AddComponent<Image>();
        panel.color = languagePanelColor;

        Text title = CreateLanguageText(rootRect, "Title", GetLocalizedInterfaceText("choose_language_title", "Scegli la lingua"), 54, FontStyle.Bold);
        title.rectTransform.anchorMin = new Vector2(0.08f, 0.64f);
        title.rectTransform.anchorMax = new Vector2(0.92f, 0.86f);

        Text subtitle = CreateLanguageText(rootRect, "Subtitle", GetLocalizedInterfaceText("choose_language_subtitle", "Seleziona una lingua per iniziare l'esperienza"), 28, FontStyle.Normal);
        subtitle.rectTransform.anchorMin = new Vector2(0.12f, 0.52f);
        subtitle.rectTransform.anchorMax = new Vector2(0.88f, 0.64f);

        CreateLanguageButton(rootRect, "Italiano", new Vector2(0.12f, 0.16f), new Vector2(0.47f, 0.42f), NureLanguage.Italian, SelectLanguageItalian);
        CreateLanguageButton(rootRect, "English", new Vector2(0.53f, 0.16f), new Vector2(0.88f, 0.42f), NureLanguage.English, SelectLanguageEnglish);

        UpdateLanguageSelectionPose();
        #pragma warning restore CS0162
    }

    private void BuildLanguageSelection3D()
    {
        Transform head = ResolveLanguageHeadTransform();

        _languageSelectionRoot = new GameObject("NureLanguageSelection3D");
        _languageSelectionTransform = _languageSelectionRoot.transform;
        _languageSelectionFixedInWorld = true;

        Vector3 origin = player != null ? player.position : Vector3.zero;
        Vector3 forward = Vector3.forward;
        Vector3 right = Vector3.right;

        if (head != null)
        {
            origin = head.position;
            forward = Vector3.ProjectOnPlane(head.forward, Vector3.up).normalized;
            right = Vector3.ProjectOnPlane(head.right, Vector3.up).normalized;
        }

        if (forward.sqrMagnitude < 0.0001f)
            forward = Vector3.forward;
        if (right.sqrMagnitude < 0.0001f)
            right = Vector3.Cross(Vector3.up, forward).normalized;

        Vector3 center = origin + (forward * 0.55f) + (Vector3.down * 0.28f);
        _languageSelectionTransform.SetPositionAndRotation(center, Quaternion.LookRotation(forward, Vector3.up));
        _languageSelectionTransform.localScale = Vector3.one;

        Texture italianTexture = ResolveLanguageFlagTexture(NureLanguage.Italian);
        Texture englishTexture = ResolveLanguageFlagTexture(NureLanguage.English);
        _italianLanguageMaterial = CreateRuntimeLanguageMaterial(Color.white, italianTexture);
        _englishLanguageMaterial = CreateRuntimeLanguageMaterial(Color.white, englishTexture);

        _italianLanguageButton = CreateLanguageButton3D(
            "Italiano_Bandiera",
            NureLanguage.Italian,
            new Vector3(-0.31f, 0f, 0f),
            _italianLanguageMaterial);
        _englishLanguageButton = CreateLanguageButton3D(
            "English_Flag",
            NureLanguage.English,
            new Vector3(0.31f, 0f, 0f),
            _englishLanguageMaterial);
    }

    private TextMesh CreateLanguageText3D(string name, string text, Vector3 localPosition, float characterSize, FontStyle style)
    {
        GameObject go = new GameObject(name, typeof(MeshRenderer), typeof(TextMesh));
        go.transform.SetParent(_languageSelectionTransform, false);
        go.transform.localPosition = localPosition;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = Vector3.one;

        TextMesh mesh = go.GetComponent<TextMesh>();
        mesh.text = text;
        mesh.anchor = TextAnchor.MiddleCenter;
        mesh.alignment = TextAlignment.Center;
        mesh.characterSize = characterSize;
        mesh.fontSize = 96;
        mesh.fontStyle = style;
        mesh.color = languageTextColor;

        Renderer renderer = go.GetComponent<Renderer>();
        if (renderer != null)
            renderer.sortingOrder = 6100;

        return mesh;
    }

    private NureLanguageSelectionButton CreateLanguageButton3D(string objectName, NureLanguage language, Vector3 localPosition, Material buttonMaterial)
    {
        GameObject buttonRoot = new GameObject(objectName, typeof(MeshFilter), typeof(MeshRenderer), typeof(BoxCollider));
        buttonRoot.name = objectName;
        buttonRoot.transform.SetParent(_languageSelectionTransform, false);
        buttonRoot.transform.localPosition = localPosition;
        buttonRoot.transform.localRotation = Quaternion.identity;
        buttonRoot.transform.localScale = new Vector3(LanguageFlagPlaneWidth, LanguageFlagPlaneHeight, 1f);

        MeshFilter meshFilter = buttonRoot.GetComponent<MeshFilter>();
        if (meshFilter != null)
            meshFilter.sharedMesh = EnsureLanguageFlagPlaneMesh();

        BoxCollider boxCollider = buttonRoot.GetComponent<BoxCollider>();
        boxCollider.size = new Vector3(1f, 1f, LanguageFlagPlaneColliderDepth);
        boxCollider.isTrigger = false;

        Rigidbody rb = buttonRoot.GetComponent<Rigidbody>();
        if (rb == null)
            rb = buttonRoot.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;

        Renderer renderer = buttonRoot.GetComponent<Renderer>();
        if (renderer != null)
            renderer.sharedMaterial = buttonMaterial;

        if (buttonRoot.GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRSimpleInteractable>() == null)
            buttonRoot.AddComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRSimpleInteractable>();

        NureLanguageSelectionButton languageButton = buttonRoot.GetComponent<NureLanguageSelectionButton>();
        if (languageButton == null)
            languageButton = buttonRoot.AddComponent<NureLanguageSelectionButton>();

        languageButton.Configure(this, language, player);
        languageButton.worldColliderSize = new Vector3(1f, 1f, LanguageFlagPlaneColliderDepth);
        languageButton.pinchPressDistance = 0.055f;
        languageButton.pinchReleaseDistance = 0.075f;
        languageButton.pinchTargetPadding = 0.06f;
        languageButton.RefreshInteractableShape();
        return languageButton;
    }

    private Mesh EnsureLanguageFlagPlaneMesh()
    {
        if (_languageFlagPlaneMesh != null)
            return _languageFlagPlaneMesh;

        _languageFlagPlaneMesh = new Mesh
        {
            name = "Runtime_LanguageFlagPlane"
        };

        _languageFlagPlaneMesh.vertices = new[]
        {
            new Vector3(-0.5f, -0.5f, 0f),
            new Vector3(0.5f, -0.5f, 0f),
            new Vector3(0.5f, 0.5f, 0f),
            new Vector3(-0.5f, 0.5f, 0f)
        };
        _languageFlagPlaneMesh.uv = new[]
        {
            new Vector2(0f, 0f),
            new Vector2(1f, 0f),
            new Vector2(1f, 1f),
            new Vector2(0f, 1f)
        };
        _languageFlagPlaneMesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
        _languageFlagPlaneMesh.RecalculateBounds();
        _languageFlagPlaneMesh.RecalculateNormals();
        return _languageFlagPlaneMesh;
    }

    private void UpdateLanguageSelectionFallbackInput()
    {
        if (!_waitingForLanguageSelection || _languageSelectionRoot == null)
            return;

        if (_languageSelectionInputLockedForPreload)
            return;

        if (TrySelectLanguageFromProximityZone())
            return;

        if (TrySelectLanguageFromHandTouch())
            return;

        _focusedLanguageButton = FindFocusedLanguageButton();
        bool pressed = IsLanguageSelectionFallbackPressed();

        if (pressed && !_previousLanguageSelectionFallbackPressed && _focusedLanguageButton != null)
        {
            if (Time.unscaledTime - _lastLanguageSelectionFallbackPressTime >= 0.2f)
            {
                _lastLanguageSelectionFallbackPressTime = Time.unscaledTime;
                _focusedLanguageButton.PressNow();
            }
        }

        _previousLanguageSelectionFallbackPressed = pressed;
    }

    private bool TrySelectLanguageFromProximityZone()
    {
        if (Time.unscaledTime - _lastLanguageSelectionFallbackPressTime < 0.2f)
            return false;

        if (_italianLanguageButton == null && _englishLanguageButton == null)
            return false;

        if (!TryGetBestSelectorPointerPosition(out Vector3 pointerPosition))
            return false;

        const float selectionRadius = 0.3f;
        float italianDistance = _italianLanguageButton != null
            ? Vector3.Distance(pointerPosition, _italianLanguageButton.transform.position)
            : float.PositiveInfinity;
        float englishDistance = _englishLanguageButton != null
            ? Vector3.Distance(pointerPosition, _englishLanguageButton.transform.position)
            : float.PositiveInfinity;

        if (italianDistance > selectionRadius && englishDistance > selectionRadius)
            return false;

        _lastLanguageSelectionFallbackPressTime = Time.unscaledTime;

        if (italianDistance <= englishDistance)
            _italianLanguageButton.PressNow();
        else
            _englishLanguageButton.PressNow();

        return true;
    }

    private bool TryGetBestSelectorPointerPosition(out Vector3 pointerPosition)
    {
        pointerPosition = Vector3.zero;

        if (TryGetNearestControllerPosition(out pointerPosition))
            return true;

        bool hasLeft = XRHandsPinchUtility.TryGetPinchData(XRHandsPinchUtility.HandSide.Left, player, out XRHandsPinchUtility.PinchData left);
        bool hasRight = XRHandsPinchUtility.TryGetPinchData(XRHandsPinchUtility.HandSide.Right, player, out XRHandsPinchUtility.PinchData right);

        if (hasLeft && hasRight)
        {
            pointerPosition = GetNearestLanguageSelectorPoint(left.indexTipWorld, right.indexTipWorld);
            return true;
        }

        if (hasLeft)
        {
            pointerPosition = left.indexTipWorld;
            return true;
        }

        if (hasRight)
        {
            pointerPosition = right.indexTipWorld;
            return true;
        }

        return false;
    }

    private Vector3 GetNearestLanguageSelectorPoint(Vector3 a, Vector3 b)
    {
        float aDistance = GetNearestLanguageSelectorDistance(a);
        float bDistance = GetNearestLanguageSelectorDistance(b);
        return aDistance <= bDistance ? a : b;
    }

    private float GetNearestLanguageSelectorDistance(Vector3 point)
    {
        float best = float.PositiveInfinity;
        if (_italianLanguageButton != null)
            best = Mathf.Min(best, Vector3.Distance(point, _italianLanguageButton.transform.position));
        if (_englishLanguageButton != null)
            best = Mathf.Min(best, Vector3.Distance(point, _englishLanguageButton.transform.position));
        return best;
    }

    private bool TryGetNearestControllerPosition(out Vector3 position)
    {
        bool hasLeft = TryGetControllerPosition(XRNode.LeftHand, out Vector3 left);
        bool hasRight = TryGetControllerPosition(XRNode.RightHand, out Vector3 right);

        if (hasLeft && hasRight)
        {
            position = GetNearestLanguageSelectorPoint(left, right);
            return true;
        }

        if (hasLeft)
        {
            position = left;
            return true;
        }

        if (hasRight)
        {
            position = right;
            return true;
        }

        position = Vector3.zero;
        return false;
    }

    private bool TryGetControllerPosition(XRNode node, out Vector3 position)
    {
        position = Vector3.zero;
        UnityEngine.XR.InputDevice device = InputDevices.GetDeviceAtXRNode(node);
        if (!device.isValid)
            return false;

        if (device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.isTracked, out bool isTracked) && !isTracked)
            return false;

        if (!device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.devicePosition, out position))
            return false;

        position = ResolveLikelyWorldSelectorPosition(position);
        return true;
    }

    private Vector3 ResolveLikelyWorldSelectorPosition(Vector3 rawPosition)
    {
        if (player == null)
            return rawPosition;

        Transform head = ResolveLanguageHeadTransform();
        if (head == null)
            return player.TransformPoint(rawPosition);

        Vector3 converted = player.TransformPoint(rawPosition);
        float rawDistance = (rawPosition - head.position).sqrMagnitude;
        float convertedDistance = (converted - head.position).sqrMagnitude;
        return convertedDistance + 0.000001f < rawDistance ? converted : rawPosition;
    }

    private bool TrySelectLanguageFromHandTouch()
    {
        if (Time.unscaledTime - _lastLanguageSelectionFallbackPressTime < 0.2f)
            return false;

        NureLanguageSelectionButton[] buttons = _languageSelectionRoot.GetComponentsInChildren<NureLanguageSelectionButton>(true);
        if (buttons == null || buttons.Length == 0)
            return false;

        if (TryGetTouchedLanguageButton(XRHandsPinchUtility.HandSide.Left, buttons, out NureLanguageSelectionButton leftButton)
            || TryGetTouchedLanguageButton(XRHandsPinchUtility.HandSide.Right, buttons, out leftButton))
        {
            _lastLanguageSelectionFallbackPressTime = Time.unscaledTime;
            leftButton.PressNow();
            return true;
        }

        return false;
    }

    private bool TryGetTouchedLanguageButton(XRHandsPinchUtility.HandSide side, NureLanguageSelectionButton[] buttons, out NureLanguageSelectionButton touchedButton)
    {
        touchedButton = null;

        if (!XRHandsPinchUtility.TryGetPinchData(side, player, out XRHandsPinchUtility.PinchData pinch))
            return false;

        const float touchDistance = 0.075f;
        for (int i = 0; i < buttons.Length; i++)
        {
            NureLanguageSelectionButton button = buttons[i];
            if (button == null)
                continue;

            Collider collider = button.GetComponent<Collider>();
            if (collider == null)
                continue;

            if (IsPointTouchingCollider(pinch.indexTipWorld, collider, touchDistance)
                || IsPointTouchingCollider(pinch.thumbTipWorld, collider, touchDistance)
                || IsPointTouchingCollider(pinch.pinchWorld, collider, touchDistance))
            {
                touchedButton = button;
                return true;
            }
        }

        return false;
    }

    private static bool IsPointTouchingCollider(Vector3 point, Collider collider, float maxDistance)
    {
        if (collider == null)
            return false;

        Vector3 closest = collider.ClosestPoint(point);
        return (closest - point).sqrMagnitude <= maxDistance * maxDistance;
    }

    private NureLanguageSelectionButton FindFocusedLanguageButton()
    {
        Transform head = ResolveLanguageHeadTransform();
        if (head == null)
            return null;

        Ray ray = new Ray(head.position, head.forward);
        if (Physics.Raycast(ray, out RaycastHit hit, 2f, ~0, QueryTriggerInteraction.Collide))
        {
            NureLanguageSelectionButton button = hit.collider.GetComponentInParent<NureLanguageSelectionButton>();
            if (button != null)
                return button;
        }

        NureLanguageSelectionButton[] buttons = _languageSelectionRoot.GetComponentsInChildren<NureLanguageSelectionButton>(true);
        if (buttons == null || buttons.Length == 0)
            return null;

        NureLanguageSelectionButton best = null;
        float bestScore = -1f;
        for (int i = 0; i < buttons.Length; i++)
        {
            NureLanguageSelectionButton button = buttons[i];
            if (button == null)
                continue;

            Vector3 toButton = button.transform.position - head.position;
            if (toButton.sqrMagnitude < 0.0001f)
                continue;

            float score = Vector3.Dot(head.forward, toButton.normalized);
            if (score > bestScore)
            {
                bestScore = score;
                best = button;
            }
        }

        return bestScore > 0.86f ? best : null;
    }

    private bool IsLanguageSelectionFallbackPressed()
    {
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null)
        {
            if (Keyboard.current.digit1Key.wasPressedThisFrame)
            {
                SelectLanguageItalian();
                return false;
            }
            if (Keyboard.current.digit2Key.wasPressedThisFrame)
            {
                SelectLanguageEnglish();
                return false;
            }
        }
#endif

        if (TryGetFallbackButtonPressed(XRNode.RightHand) || TryGetFallbackButtonPressed(XRNode.LeftHand))
            return true;

        return TryGetFallbackPinchPressed(XRHandsPinchUtility.HandSide.Right)
            || TryGetFallbackPinchPressed(XRHandsPinchUtility.HandSide.Left);
    }

    private static bool TryGetFallbackButtonPressed(XRNode node)
    {
        UnityEngine.XR.InputDevice device = InputDevices.GetDeviceAtXRNode(node);
        if (!device.isValid)
            return false;

        if (device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.triggerButton, out bool triggerButton) && triggerButton)
            return true;
        if (device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.primaryButton, out bool primaryButton) && primaryButton)
            return true;
        if (device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.gripButton, out bool gripButton) && gripButton)
            return true;
        if (device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.trigger, out float trigger) && trigger >= 0.65f)
            return true;

        return false;
    }

    private bool TryGetFallbackPinchPressed(XRHandsPinchUtility.HandSide side)
    {
        return XRHandsPinchUtility.TryGetPinchData(side, player, out XRHandsPinchUtility.PinchData pinch)
            && pinch.pinchDistance <= 0.045f;
    }

    private Texture2D CreateLanguageFlagTexture(NureLanguage language)
    {
        Color32[] pixels = new Color32[LanguageFlagTextureWidth * LanguageFlagTextureHeight];

        if (language == NureLanguage.Italian)
            PaintItalianFlagTexture(pixels, LanguageFlagTextureWidth, LanguageFlagTextureHeight);
        else
            PaintEnglishFlagTexture(pixels, LanguageFlagTextureWidth, LanguageFlagTextureHeight);

        Texture2D texture = new Texture2D(LanguageFlagTextureWidth, LanguageFlagTextureHeight, TextureFormat.RGBA32, false, false);
        texture.name = language == NureLanguage.Italian ? "Runtime_ItalianFlag_Texture" : "Runtime_EnglishFlag_Texture";
        texture.wrapMode = TextureWrapMode.Clamp;
        texture.filterMode = FilterMode.Bilinear;
        texture.anisoLevel = 2;
        texture.SetPixels32(pixels);
        texture.Apply(false, true);
        return texture;
    }

    private static void PaintItalianFlagTexture(Color32[] pixels, int width, int height)
    {
        for (int y = 0; y < height; y++)
        {
            int row = y * width;
            for (int x = 0; x < width; x++)
            {
                pixels[row + x] = x < width / 3
                    ? ItalianFlagGreen
                    : x < (width * 2) / 3
                        ? ItalianFlagWhite
                        : ItalianFlagRed;
            }
        }
    }

    private static void PaintEnglishFlagTexture(Color32[] pixels, int width, int height)
    {
        const float diagonalNormal = 1.118034f;

        for (int y = 0; y < height; y++)
        {
            float v = (y + 0.5f) / height;
            float py = v - 0.5f;
            int row = y * width;

            for (int x = 0; x < width; x++)
            {
                float u = (x + 0.5f) / width;
                float px = (u * 2f) - 1f;
                float diagonalA = Mathf.Abs(py - (px * 0.5f)) / diagonalNormal;
                float diagonalB = Mathf.Abs(py + (px * 0.5f)) / diagonalNormal;
                float absX = Mathf.Abs(px);
                float absY = Mathf.Abs(py);

                Color32 color = EnglishFlagBlue;

                if (diagonalA < 0.105f || diagonalB < 0.105f)
                    color = EnglishFlagWhite;
                if (diagonalA < 0.045f || diagonalB < 0.045f)
                    color = EnglishFlagRed;
                if (absX < 0.155f || absY < 0.135f)
                    color = EnglishFlagWhite;
                if (absX < 0.075f || absY < 0.065f)
                    color = EnglishFlagRed;

                pixels[row + x] = color;
            }
        }
    }

    private Material CreateRuntimeLanguageMaterial(Color color, Texture texture = null)
    {
        Shader shader =
            Shader.Find("Universal Render Pipeline/Unlit") ??
            Shader.Find("Unlit/Texture") ??
            Shader.Find("Sprites/Default") ??
            Shader.Find("Unlit/Color") ??
            Shader.Find("Standard");

        if (shader == null)
        {
            Debug.LogWarning("[SceneGroupManager] Nessuno shader disponibile per il materiale del menu lingua.");
            return null;
        }

        Material material = new Material(shader);
        material.name = texture != null ? $"Runtime_LanguageSelection_{texture.name}_Material" : "Runtime_LanguageSelection_Material";
        Color tint = texture != null ? Color.white : color;

        if (material.HasProperty("_BaseColor"))
            material.SetColor("_BaseColor", tint);
        if (material.HasProperty("_Color"))
            material.SetColor("_Color", tint);
        if (texture != null)
        {
            texture.wrapMode = TextureWrapMode.Clamp;

            if (material.HasProperty("_BaseMap"))
                material.SetTexture("_BaseMap", texture);
            if (material.HasProperty("_MainTex"))
                material.SetTexture("_MainTex", texture);
        }
        if (material.HasProperty("_Cull"))
            material.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Off);

        return material;
    }

    private Text CreateLanguageText(RectTransform parent, string name, string text, int fontSize, FontStyle style)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
        RectTransform rect = go.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        Text label = go.GetComponent<Text>();
        label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        label.text = text;
        label.fontSize = fontSize;
        label.fontStyle = style;
        label.alignment = TextAnchor.MiddleCenter;
        label.color = languageTextColor;
        label.horizontalOverflow = HorizontalWrapMode.Wrap;
        label.verticalOverflow = VerticalWrapMode.Overflow;
        return label;
    }

    private void CreateLanguageButton(RectTransform parent, string labelText, Vector2 anchorMin, Vector2 anchorMax, NureLanguage language, UnityEngine.Events.UnityAction action)
    {
        GameObject go = new GameObject($"Button - {labelText}", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button), typeof(BoxCollider), typeof(UnityEngine.XR.Interaction.Toolkit.Interactables.XRSimpleInteractable), typeof(NureLanguageSelectionButton));
        RectTransform rect = go.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        Image image = go.GetComponent<Image>();
        image.color = languageButtonColor;

        Button button = go.GetComponent<Button>();
        button.onClick.AddListener(action);

        NureLanguageSelectionButton languageButton = go.GetComponent<NureLanguageSelectionButton>();
        languageButton.Configure(this, language, player);
        languageButton.pinchPressDistance = 0.05f;
        languageButton.pinchReleaseDistance = 0.07f;
        languageButton.pinchTargetPadding = 0.1f;

        Text label = CreateLanguageText(rect, "Label", labelText, 34, FontStyle.Bold);
        label.rectTransform.anchorMin = Vector2.zero;
        label.rectTransform.anchorMax = Vector2.one;
        label.raycastTarget = false;
    }

    private void EnsureLanguageSelectionEventSystem()
    {
        EventSystem es = FindFirstObjectByType<EventSystem>();
        if (es == null)
        {
            GameObject esGo = new GameObject("EventSystem");
            es = esGo.AddComponent<EventSystem>();
        }

#if ENABLE_INPUT_SYSTEM
        if (es != null && es.GetComponent<InputSystemUIInputModule>() == null)
            es.gameObject.AddComponent<InputSystemUIInputModule>();
#endif
    }

    private void UpdateLanguageSelectionPose()
    {
        if (!_waitingForLanguageSelection || _languageSelectionTransform == null)
            return;

        if (_languageSelectionFixedInWorld)
            return;

        Transform head = ResolveLanguageHeadTransform();
        if (head == null)
            return;

        if (_languageSelectionTransform.parent != head)
        {
            _languageSelectionTransform.SetParent(head, false);

            Canvas canvas = _languageSelectionRoot != null ? _languageSelectionRoot.GetComponent<Canvas>() : null;
            if (canvas != null)
                canvas.worldCamera = head.GetComponent<Camera>();
        }

        _languageSelectionTransform.localPosition = new Vector3(0f, -0.02f, Mathf.Clamp(languagePanelDistanceFromCamera, 0.75f, 1.15f));
        _languageSelectionTransform.localRotation = Quaternion.identity;
        if (_languageSelectionRoot != null && _languageSelectionRoot.GetComponent<Canvas>() != null)
            _languageSelectionTransform.localScale = Vector3.one * 0.001f;
        else
            _languageSelectionTransform.localScale = Vector3.one;
    }

    private Transform ResolveLanguageHeadTransform()
    {
        Transform head = TryGetPlayerHeadTransform();
        if (head != null)
            return head;

        Camera cam = ResolveMainCamera();
        if (cam != null)
            return cam.transform;

        return player;
    }

    private Camera ResolveMainCamera()
    {
        if (Camera.main != null)
            return Camera.main;

        Camera[] cameras = FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < cameras.Length; i++)
        {
            Camera c = cameras[i];
            if (c != null && c.gameObject.scene.IsValid() && c.isActiveAndEnabled)
                return c;
        }

        return null;
    }

    private void RefreshLocalizedRuntimeUI()
    {
        HandRadialVirtualSceneMenu[] menus = FindObjectsByType<HandRadialVirtualSceneMenu>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < menus.Length; i++)
        {
            if (menus[i] != null)
                menus[i].RefreshButtons();
        }

        MapPopupMenuController[] popups = FindObjectsByType<MapPopupMenuController>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < popups.Length; i++)
        {
            if (popups[i] != null)
                popups[i].SyncAll();
        }
    }

    private void ScheduleLocalizedRuntimeUIRefresh()
    {
        if (_localizedRuntimeUiRefreshRoutine != null)
            StopCoroutine(_localizedRuntimeUiRefreshRoutine);

        _localizedRuntimeUiRefreshRoutine = StartCoroutine(RefreshLocalizedRuntimeUIDeferred());
    }

    private IEnumerator RefreshLocalizedRuntimeUIDeferred()
    {
        yield return null;
        RefreshLocalizedRuntimeUI();
        _localizedRuntimeUiRefreshRoutine = null;
    }

    // --- API PUBBLICA ---

    public void PlayFromStart()
    {
        StopSequence();
        _index = -1;

        if (verboseLogs) Debug.Log("[SceneGroupManager] PlayFromStart()");
        _sequenceRoutine = StartCoroutine(SequenceRoutine());
    }

    public void StopSequence()
    {
        if (_sequenceRoutine != null)
        {
            StopCoroutine(_sequenceRoutine);
            _sequenceRoutine = null;
        }
    }

    public void Next()
    {
        // Se vuoi “saltare” manualmente
        if (_sequenceRoutine == null)
        {
            if (verboseLogs) Debug.Log("[SceneGroupManager] Next() -> Sequence non attiva. Avvio SequenceRoutine.");
            _sequenceRoutine = StartCoroutine(SequenceRoutine());
        }
        else
        {
            // forza passaggio: ferma l'audio (se presente) e il video (se presente nella scena corrente)
            if (audioSource != null) audioSource.Stop();

            var cfg = GetCurrentCfg();
            if (cfg != null && cfg.videoPlayer != null)
            {
                try { cfg.videoPlayer.Stop(); } catch { }
            }
        }
    }

    public void ActivateScene(GameObject sceneRootOrChild)
    {
        if (sceneRootOrChild == null)
        {
            Debug.LogWarning("[SceneGroupManager] ActivateScene: parametro NULL.");
            return;
        }

        VirtualScene cfg = FindConfigForObject(sceneRootOrChild);

        if (cfg == null || cfg.root == null)
        {
            Debug.LogWarning($"[SceneGroupManager] ActivateScene: nessuna VirtualScene trovata per '{sceneRootOrChild.name}'. Controlla la lista 'scenes'.");
            return;
        }

        StopSequence();
        ActivateOnly(cfg);

        if (verboseLogs) Debug.Log($"[SceneGroupManager] ActivateScene -> {cfg.root.name}");
        StartCoroutine(ApplySpawnAudioVideoRoutine(cfg));
    }

    public void DeactivateAllScenes()
    {
        StopScene7DelayedDissolve(restoreTarget: true);

        foreach (var s in scenes)
        {
            if (s.root != null && s.root.activeSelf)
                s.root.SetActive(false);

            // spegni eventuali pannelli video
            if (s.videoPanelRoot != null && s.videoPanelRoot.activeSelf)
                s.videoPanelRoot.SetActive(false);
        }
    }

    // --- CORE SEQUENCE ---

    private IEnumerator SequenceRoutine()
    {
        if (scenes == null || scenes.Count == 0)
        {
            Debug.LogWarning("[SceneGroupManager] Nessuna scena in lista.");
            yield break;
        }

        while (true)
        {
            _index++;

            if (_index >= scenes.Count)
            {
                if (verboseLogs) Debug.Log("[SceneGroupManager] Fine lista scene.");
                _sequenceRoutine = null;
                yield break;
            }

            var cfg = scenes[_index];
            if (cfg == null || cfg.root == null)
            {
                Debug.LogWarning($"[SceneGroupManager] Scene[{_index}] invalida (cfg o root null).");
                yield return null;
                continue;
            }

            ActivateOnly(cfg);

            if (verboseLogs) Debug.Log($"[SceneGroupManager] Sequence -> attivo: {cfg.root.name} (index {_index})");

            // spawn + audio + video (se presente)
            yield return ApplySpawnAudioVideoRoutine(cfg);

            // 1) Se la scena richiede attesa video, resta finché finisce
            if (cfg.waitForVideoEnd && cfg.videoPlayer != null)
            {
                yield return WaitForVideoEnd(cfg.videoPlayer);
                continue;
            }

            // 2) Altrimenti, se c'è audioClip, aspetta audio
            AudioClip localizedClip = ResolveAudioClip(cfg);
            if (audioSource != null && playAudioOnSwitch && localizedClip != null)
            {
                if (waitUsingIsPlaying)
                {
                    while (audioSource != null && audioSource.isPlaying)
                        yield return null;
                }
                else
                {
                    yield return new WaitForSeconds(localizedClip.length);
                }
            }
            else
            {
                // micro-wait per evitare loop “istantanei” se mancano clip
                yield return new WaitForSeconds(0.05f);
            }
        }
    }

    private IEnumerator ApplySpawnAudioVideoRoutine(VirtualScene cfg)
    {
        if (cfg == null)
            yield break;

        // --- Spawn (se null, non teleporta: utile per Scena10 "stessa posizione della 9")
        if (player != null && cfg.playerSpawn != null)
        {
            ApplyPlayerPoseFromSpawn(cfg, cfg.playerSpawn);

            if (verboseLogs)
                Debug.Log($"[SceneGroupManager] Teleport -> scene={cfg.root?.name} spawn={cfg.playerSpawn.name} pos={cfg.playerSpawn.position}");
        }

        // --- Audio clip scene (non video)
        if (audioSource != null && stopAudioOnSwitch)
            audioSource.Stop();

        AudioClip localizedClip = ResolveAudioClip(cfg);
        if (audioSource != null && playAudioOnSwitch && localizedClip != null)
        {
            audioSource.clip = localizedClip;

            if (audioStartDelay > 0f)
                yield return new WaitForSeconds(audioStartDelay);

            audioSource.Play();

            if (verboseLogs)
            {
                Debug.Log($"[SceneGroupManager] PLAY audio '{localizedClip.name}' scene={cfg.root?.name} lang={currentLanguage} " +
                          $"isPlaying={audioSource.isPlaying} vol={audioSource.volume} spatialBlend={audioSource.spatialBlend}");
            }
        }

        // --- Video (tipicamente Scena10)
        // Spegni tutti i pannelli video delle altre scene (già fatto in ActivateOnly, ma sicuro)
        if (cfg.videoPanelRoot != null)
            cfg.videoPanelRoot.SetActive(true);

        if (cfg.videoPlayer != null)
        {
            EnsureVideoOutputSetup(cfg.videoPlayer);

            if (videoStartDelay > 0f)
                yield return new WaitForSeconds(videoStartDelay);

            // Avvio robusto: Stop -> Prepare -> Play
            try { cfg.videoPlayer.Stop(); } catch { }

            if (!cfg.videoPlayer.isPrepared)
            {
                cfg.videoPlayer.Prepare();
                float t = 0f;
                while (!cfg.videoPlayer.isPrepared && t < 2f)
                {
                    t += Time.unscaledDeltaTime;
                    yield return null;
                }
            }

            EnsureVideoTargetTextureMatchesVideo(cfg.videoPlayer);
            cfg.videoPlayer.Play();

            if (verboseLogs)
                Debug.Log($"[SceneGroupManager] PLAY VIDEO '{cfg.videoPlayer.clip?.name}' scene={cfg.root?.name} isPlaying={cfg.videoPlayer.isPlaying}");
        }

        TryStartDonnaScene6Sequence(cfg);
    }

    private void ApplyPlayerPoseFromSpawn(VirtualScene cfg, Transform spawn)
    {
        if (player == null || spawn == null)
            return;

        bool isScene1 = IsScene1Config(cfg);
        bool shouldFacePlastico = isScene1 && scene1FacePlastico;
        Vector3 desiredForward = GetDesiredForwardFromSpawn(spawn, shouldFacePlastico);

        if (isScene1 && forceScene1PlayerPose)
            ShowScene1ViewBlockerIfNeeded();

        // Passo 1: imposta orientamento base del rig.
        Quaternion targetYaw = Quaternion.LookRotation(desiredForward, Vector3.up);
        player.SetPositionAndRotation(spawn.position, targetYaw);

        // Passo 2: compensa offset/yaw reale della HMD, cosi' la testa finisce davvero allo spawn.
        Transform head = TryGetPlayerHeadTransform();
        if (head != null && head != player)
        {
            float desiredYaw = targetYaw.eulerAngles.y;
            float headYaw = head.eulerAngles.y;
            float yawDelta = Mathf.DeltaAngle(headYaw, desiredYaw);
            player.Rotate(0f, yawDelta, 0f, Space.World);

            Vector3 headHorizontalOffset = head.position - player.position;
            headHorizontalOffset.y = 0f;

            Vector3 targetPos = spawn.position - headHorizontalOffset;
            targetPos.y = spawn.position.y;
            player.position = targetPos;
        }

        if (isScene1 && forceScene1PlayerPose)
            StartScene1PoseRoutine(spawn);
        else
            StopScene1PoseRoutine();
    }

    private void StartScene1PoseRoutine(Transform spawn)
    {
        StopScene1PoseRoutine();
        _scene1PoseRoutine = StartCoroutine(ForceScene1PoseRoutine(spawn));
    }

    private void StopScene1PoseRoutine()
    {
        if (_scene1PoseRoutine == null)
            return;

        StopCoroutine(_scene1PoseRoutine);
        _scene1PoseRoutine = null;
    }

    private void ShowScene1ViewBlockerIfNeeded()
    {
        if (_suppressScene1ViewBlocker)
            return;

        if (!hideStartupScene1PoseAdjustments)
            return;

        if (!hideScene1PoseAdjustmentsOnEveryEntry && _hasShownStartupScene1ViewBlocker)
            return;

        if (!EnsureStartupViewBlockerCanvas())
            return;

        _hasShownStartupScene1ViewBlocker = true;

        if (_startupViewBlockerRoutine != null)
            StopCoroutine(_startupViewBlockerRoutine);

        _startupViewBlockerRoutine = StartCoroutine(StartupViewBlockerRoutine(
            Mathf.Max(0f, scene1PoseReapplySeconds + startupScene1BlockExtraSeconds)));
    }

    private IEnumerator StartupViewBlockerRoutine(float holdSeconds)
    {
        if (_startupViewBlockerCanvasGroup == null && !EnsureStartupViewBlockerCanvas())
        {
            _startupViewBlockerRoutine = null;
            yield break;
        }

        _startupViewBlockerCanvasGroup.alpha = 1f;

        if (holdSeconds > 0f)
            yield return new WaitForSecondsRealtime(holdSeconds);

        float fadeSeconds = Mathf.Max(0.01f, startupScene1UnblockFadeSeconds);
        float elapsed = 0f;
        while (elapsed < fadeSeconds)
        {
            if (_startupViewBlockerCanvasGroup == null)
            {
                _startupViewBlockerRoutine = null;
                yield break;
            }

            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / fadeSeconds);
            _startupViewBlockerCanvasGroup.alpha = 1f - t;
            yield return null;
        }

        _startupViewBlockerRoutine = null;
        DestroyStartupViewBlockerObject();
    }

    private bool EnsureStartupViewBlockerCanvas()
    {
        Transform head = TryGetPlayerHeadTransform();
        if (head == null)
            return false;

        if (_startupViewBlockerTransform != null && _startupViewBlockerCanvasGroup != null)
        {
            if (_startupViewBlockerTransform.parent != head)
                _startupViewBlockerTransform.SetParent(head, false);

            _startupViewBlockerTransform.localPosition = new Vector3(0f, 0f, 0.35f);
            _startupViewBlockerTransform.localRotation = Quaternion.identity;
            return true;
        }

        GameObject canvasGo = new GameObject("StartupViewBlocker");
        _startupViewBlockerTransform = canvasGo.transform;
        _startupViewBlockerTransform.SetParent(head, false);
        _startupViewBlockerTransform.localPosition = new Vector3(0f, 0f, 0.35f);
        _startupViewBlockerTransform.localRotation = Quaternion.identity;
        _startupViewBlockerTransform.localScale = Vector3.one * 0.001f;

        Camera headCamera = head.GetComponent<Camera>();
        Canvas canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = headCamera;
        canvas.sortingOrder = 7000;

        CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.dynamicPixelsPerUnit = 10f;

        GraphicRaycaster raycaster = canvasGo.AddComponent<GraphicRaycaster>();
        raycaster.enabled = false;

        _startupViewBlockerCanvasGroup = canvasGo.AddComponent<CanvasGroup>();
        _startupViewBlockerCanvasGroup.alpha = 1f;
        _startupViewBlockerCanvasGroup.interactable = false;
        _startupViewBlockerCanvasGroup.blocksRaycasts = false;

        RectTransform canvasRect = canvas.GetComponent<RectTransform>();
        canvasRect.sizeDelta = new Vector2(3200f, 3200f);

        GameObject imageGo = new GameObject("Blocker");
        imageGo.transform.SetParent(canvasGo.transform, false);

        RectTransform imageRect = imageGo.AddComponent<RectTransform>();
        imageRect.anchorMin = Vector2.zero;
        imageRect.anchorMax = Vector2.one;
        imageRect.offsetMin = Vector2.zero;
        imageRect.offsetMax = Vector2.zero;

        RawImage image = imageGo.AddComponent<RawImage>();
        image.texture = Texture2D.whiteTexture;
        image.color = Color.black;
        image.raycastTarget = false;
        return true;
    }

    private void StopAndDestroyStartupViewBlocker()
    {
        if (_startupViewBlockerRoutine != null)
        {
            StopCoroutine(_startupViewBlockerRoutine);
            _startupViewBlockerRoutine = null;
        }

        DestroyStartupViewBlockerObject();
    }

    private void DestroyStartupViewBlockerObject()
    {
        if (_startupViewBlockerTransform != null)
            Destroy(_startupViewBlockerTransform.gameObject);

        _startupViewBlockerTransform = null;
        _startupViewBlockerCanvasGroup = null;
    }

    private IEnumerator ForceScene1PoseRoutine(Transform spawn)
    {
        if (spawn == null || player == null)
            yield break;

        int appliedFrames = 0;
        float endTime = Time.unscaledTime + scene1PoseReapplySeconds;

        while (appliedFrames < scene1PoseReapplyFrames && Time.unscaledTime <= endTime)
        {
            ApplyPlayerPoseOnceForScene1(spawn);
            appliedFrames++;
            yield return null;
        }

        _scene1PoseRoutine = null;
    }

    private void ApplyPlayerPoseOnceForScene1(Transform spawn)
    {
        if (spawn == null || player == null)
            return;

        Vector3 desiredForward = GetDesiredForwardFromSpawn(spawn, scene1FacePlastico);
        float desiredYaw = Quaternion.LookRotation(desiredForward, Vector3.up).eulerAngles.y;

        Transform head = TryGetPlayerHeadTransform();
        if (head != null && head != player)
        {
            float yawDelta = Mathf.DeltaAngle(head.eulerAngles.y, desiredYaw);
            player.Rotate(0f, yawDelta, 0f, Space.World);

            Vector3 headHorizontalOffset = head.position - player.position;
            headHorizontalOffset.y = 0f;

            Vector3 targetPos = spawn.position - headHorizontalOffset;
            targetPos.y = spawn.position.y;
            player.position = targetPos;
            return;
        }

        player.SetPositionAndRotation(spawn.position, Quaternion.Euler(0f, desiredYaw, 0f));
    }

    private Vector3 GetDesiredForwardFromSpawn(Transform spawn, bool facePlastico)
    {
        if (spawn == null)
            return Vector3.forward;

        Vector3 desiredForward = spawn.forward;
        desiredForward.y = 0f;

        if (facePlastico)
        {
            Transform plastico = FindFirstTransformByExactName(ProtectedScene1ObjectName);
            if (plastico != null)
            {
                Vector3 toPlastico = plastico.position - spawn.position;
                toPlastico.y = 0f;
                if (toPlastico.sqrMagnitude > 0.0001f)
                    desiredForward = toPlastico.normalized;
            }
        }

        if (desiredForward.sqrMagnitude <= 0.0001f)
            desiredForward = Vector3.forward;

        return desiredForward.normalized;
    }

    private Transform TryGetPlayerHeadTransform()
    {
        if (player == null)
            return null;

        Camera cam = player.GetComponentInChildren<Camera>(true);
        if (cam != null)
            return cam.transform;

        Camera mainCam = Camera.main;
        if (mainCam != null && mainCam.transform.IsChildOf(player))
            return mainCam.transform;

        return player;
    }

    private Transform FindFirstTransformByExactName(string exactName)
    {
        if (string.IsNullOrEmpty(exactName))
            return null;

        List<GameObject> matches = FindSceneObjectsByExactName(exactName);
        for (int i = 0; i < matches.Count; i++)
        {
            GameObject go = matches[i];
            if (go == null)
                continue;

            return go.transform;
        }

        return null;
    }

    private bool IsScene1Config(VirtualScene cfg)
    {
        if (cfg == null)
            return false;

        string sceneName = string.IsNullOrWhiteSpace(cfg.name) ? cfg.root?.name : cfg.name;
        return string.Equals(sceneName, ForcedScene1Name, StringComparison.OrdinalIgnoreCase);
    }

    private void EnsureVideoOutputSetup(VideoPlayer vp)
    {
        if (vp == null)
            return;

        // Audio: ensure track 0 is enabled and routed to an AudioSource.
        AudioSource targetSource = vp.GetTargetAudioSource(0);
        if (targetSource == null)
        {
            targetSource = vp.GetComponent<AudioSource>();
            if (targetSource == null)
                targetSource = vp.gameObject.AddComponent<AudioSource>();
        }

        targetSource.playOnAwake = false;
        targetSource.mute = false;
        targetSource.volume = 1f;
        targetSource.spatialBlend = 0f;

        vp.audioOutputMode = VideoAudioOutputMode.AudioSource;
        vp.aspectRatio = VideoAspectRatio.FitInside;
        vp.controlledAudioTrackCount = 1;
        vp.EnableAudioTrack(0, true);
        vp.SetTargetAudioSource(0, targetSource);
        vp.SetDirectAudioMute(0, false);
        vp.SetDirectAudioVolume(0, 1f);

        // Video: if target texture is missing, try to reuse the material RenderTexture.
        if (vp.targetTexture == null)
        {
            Renderer r = vp.GetComponent<Renderer>();
            Material m = r != null ? r.sharedMaterial : null;
            RenderTexture rt = null;

            if (m != null)
            {
                if (m.HasProperty("_BaseMap"))
                    rt = m.GetTexture("_BaseMap") as RenderTexture;
                if (rt == null && m.HasProperty("_MainTex"))
                    rt = m.GetTexture("_MainTex") as RenderTexture;
            }

            if (rt != null)
            {
                vp.renderMode = VideoRenderMode.RenderTexture;
                vp.targetTexture = rt;
            }
        }

        if (verboseLogs)
            Debug.Log($"[SceneGroupManager] Video setup -> mode={vp.audioOutputMode} targetAudio={(targetSource != null ? targetSource.name : "NULL")} trackCount={vp.controlledAudioTrackCount}");
    }

    private void EnsureVideoTargetTextureMatchesVideo(VideoPlayer vp)
    {
        if (vp == null)
            return;

        uint srcW = vp.width;
        uint srcH = vp.height;
        if (srcW == 0 || srcH == 0)
            return;

        int targetW = Mathf.Max(256, (int)srcW);
        int targetH = Mathf.Max(256, (int)srcH);
        int maxTargetSize = Application.isMobilePlatform ? MobileVideoRenderTextureMaxSize : DefaultVideoRenderTextureMaxSize;
        int maxDimension = Mathf.Max(targetW, targetH);
        if (maxDimension > maxTargetSize)
        {
            float scale = maxTargetSize / (float)maxDimension;
            targetW = Mathf.Max(256, Mathf.RoundToInt(targetW * scale));
            targetH = Mathf.Max(256, Mathf.RoundToInt(targetH * scale));
        }

        RenderTexture rt;
        if (_runtimeVideoTextures.TryGetValue(vp, out rt))
        {
            if (rt != null && rt.width == targetW && rt.height == targetH)
                return;

            if (rt != null)
                rt.Release();
        }

        rt = new RenderTexture(targetW, targetH, 0, RenderTextureFormat.ARGB32);
        rt.name = $"RT_{vp.gameObject.name}_{targetW}x{targetH}";
        rt.filterMode = FilterMode.Bilinear;
        rt.wrapMode = TextureWrapMode.Clamp;
        rt.Create();
        _runtimeVideoTextures[vp] = rt;

        vp.renderMode = VideoRenderMode.RenderTexture;
        vp.targetTexture = rt;

        Renderer renderer = vp.GetComponent<Renderer>();
        if (renderer != null)
        {
            Material mat = renderer.material;
            if (mat != null)
            {
                if (mat.HasProperty("_BaseMap"))
                    mat.SetTexture("_BaseMap", rt);
                if (mat.HasProperty("_MainTex"))
                    mat.SetTexture("_MainTex", rt);
            }
        }

        if (verboseLogs)
            Debug.Log($"[SceneGroupManager] Video RT auto-fit -> {targetW}x{targetH} ({vp.gameObject.name})");

        ApplyVideoPlaneAspect(vp, targetW, targetH);
    }

    private void ReleaseRuntimeVideoTextures()
    {
        foreach (var kv in _runtimeVideoTextures)
        {
            if (kv.Value != null)
                kv.Value.Release();
        }

        _runtimeVideoTextures.Clear();
        _videoPlaneBaseScales.Clear();
    }

    private void ApplyVideoPlaneAspect(VideoPlayer vp, int textureWidth, int textureHeight)
    {
        if (vp == null || textureWidth <= 0 || textureHeight <= 0)
            return;

        Transform t = vp.transform;
        if (t == null)
            return;

        if (!_videoPlaneBaseScales.TryGetValue(vp, out Vector3 baseScale))
        {
            baseScale = t.localScale;
            _videoPlaneBaseScales[vp] = baseScale;
        }

        float videoAspect = textureWidth / (float)textureHeight;
        float panelWidth = Mathf.Max(0.0001f, baseScale.x);
        float panelHeight = Mathf.Max(0.0001f, baseScale.z);
        float panelAspect = panelWidth / panelHeight;
        Vector3 adjusted = baseScale;

        // Fit-inside: keep the video fully visible inside the original panel bounds.
        // Never enlarge beyond base scale, so the plane does not clip into nearby geometry.
        if (videoAspect >= panelAspect)
        {
            adjusted.x = panelWidth;
            adjusted.z = panelWidth / Mathf.Max(0.01f, videoAspect);
        }
        else
        {
            adjusted.z = panelHeight;
            adjusted.x = panelHeight * videoAspect;
        }

        t.localScale = adjusted;

        if (verboseLogs)
            Debug.Log($"[SceneGroupManager] Video plane fit-inside -> videoAspect={videoAspect:0.###}, panelAspect={panelAspect:0.###}, scale={adjusted}");
    }

    private IEnumerator WaitForVideoEnd(VideoPlayer vp)
    {
        if (vp == null) yield break;

        bool ended = false;
        void OnEnd(VideoPlayer _)
        {
            ended = true;
        }

        vp.loopPointReached += OnEnd;

        // Attendi finché:
        // - arriva loopPointReached
        // - oppure vp smette di essere playing (fallback)
        // - oppure se non ha clip/length, non bloccare per sempre
        float safety = 0f;
        float safetyMax = 600f; // 10 minuti hard safety

        while (!ended)
        {
            safety += Time.unscaledDeltaTime;
            if (safety > safetyMax) break;

            if (vp.clip == null && !vp.isPlaying) break;
            if (vp.clip != null && !vp.isPlaying && vp.time > 0.1f) break;

            yield return null;
        }

        vp.loopPointReached -= OnEnd;

        if (verboseLogs) Debug.Log("[SceneGroupManager] Video ended -> continue sequence.");
    }

    private void ActivateOnly(VirtualScene cfg)
    {
        StopScene7DelayedDissolve(restoreTarget: true);
        StopScene8ChurchDelayedFade(restoreState: true);
        StopEpigrafeEntrance();
        StopDonnaSequence();

        foreach (var s in scenes)
        {
            if (s.root != null)
            {
                bool shouldBeActive = s.root == cfg.root;
                if (s.root.activeSelf != shouldBeActive)
                    s.root.SetActive(shouldBeActive);
            }

            if (s.videoPanelRoot != null && s.videoPanelRoot.activeSelf)
                s.videoPanelRoot.SetActive(false);
        }

        ApplySceneSpecificVisibility(cfg);
        TryStartEpigrafeScene3Entrance(cfg);
        EnsureScene6StaticTorchFlames(cfg);
        TryStartScene8ChurchDelayedFade(cfg);
        TryPlaySceneSpawnDissolveFx(cfg);
        SceneActivated?.Invoke(cfg);
    }

    private void TryPlaySceneSpawnDissolveFx(VirtualScene cfg)
    {
        if (_suppressNextSceneSpawnDissolve)
        {
            _suppressNextSceneSpawnDissolve = false;
            StopScene7DelayedDissolve(restoreTarget: true);

            if (_sceneSpawnDissolveVfx != null)
                _sceneSpawnDissolveVfx.StopAndClear();

            return;
        }

        if (cfg == null
            || cfg.root == null
            || !enableSceneSpawnDissolveFx
            || (disableSceneSpawnDissolveOnMobile && Application.isMobilePlatform))
        {
            StopScene7DelayedDissolve(restoreTarget: true);

            if (_sceneSpawnDissolveVfx != null)
                _sceneSpawnDissolveVfx.StopAndClear();
            return;
        }

        if (_sceneSpawnDissolveVfx == null)
        {
            _sceneSpawnDissolveVfx = GetComponent<SceneActivationUrpDissolveVfx>();
            if (_sceneSpawnDissolveVfx == null)
                _sceneSpawnDissolveVfx = gameObject.AddComponent<SceneActivationUrpDissolveVfx>();
        }

        string sceneName = string.IsNullOrWhiteSpace(cfg.name) ? cfg.root.name : cfg.name;
        SceneActivationUrpDissolveVfx.Settings settings = new SceneActivationUrpDissolveVfx.Settings
        {
            maxRenderers = sceneSpawnDissolveMaxRenderers,
            duration = sceneSpawnDissolveDuration,
            groupStagger = sceneSpawnDissolveGroupStagger,
            minBoundsSize = sceneSpawnDissolveMinBoundsSize,
            edgeWidth = sceneSpawnDissolveEdgeWidth,
            edgeColorIntensity = sceneSpawnDissolveEdgeIntensity,
            noiseScale = sceneSpawnDissolveNoiseScale,
            edgeColor = sceneSpawnDissolveEdgeColor,
        };

        if (Application.isMobilePlatform)
        {
            settings.maxRenderers = Mathf.Min(settings.maxRenderers, MobileSceneSpawnDissolveMaxRenderers);
            settings.duration = Mathf.Min(settings.duration, MobileSceneSpawnDissolveDuration);
            settings.minBoundsSize = Mathf.Max(settings.minBoundsSize, MobileSceneSpawnDissolveMinBoundsSize);
        }

        Transform referencePoint = cfg.playerSpawn != null ? cfg.playerSpawn : player;
        PrepareScene7DelayedDissolveTarget(cfg, sceneName);
        float initialDissolveDuration = _sceneSpawnDissolveVfx.Play(cfg.root, referencePoint, settings);
        TryStartScene7DelayedDissolve(cfg, sceneName, referencePoint, settings, initialDissolveDuration);

        if (verboseLogs)
            Debug.Log($"[SceneGroupManager] Scene spawn dissolve FX -> {cfg.root.name}");
    }

    private void PrepareScene7DelayedDissolveTarget(VirtualScene cfg, string sceneName)
    {
        StopScene7DelayedDissolve(restoreTarget: true);

        if (cfg == null || cfg.root == null)
            return;

        if (!string.Equals(sceneName, Scene7DelayedDissolveSceneName, StringComparison.OrdinalIgnoreCase))
            return;

        Transform target = FindChildByExactName(cfg.root.transform, Scene7DelayedDissolveObjectName);
        if (target == null)
            return;

        _scene7DelayedDissolveTarget = target.gameObject;
        _scene7DelayedDissolveTargetOriginalActive = _scene7DelayedDissolveTarget.activeSelf;
        _scene7DelayedDissolveTarget.SetActive(false);
    }

    private void TryStartScene7DelayedDissolve(VirtualScene cfg, string sceneName, Transform referencePoint, SceneActivationUrpDissolveVfx.Settings settings, float initialDissolveDuration)
    {
        if (cfg == null || cfg.root == null || _scene7DelayedDissolveTarget == null)
            return;

        if (!string.Equals(sceneName, Scene7DelayedDissolveSceneName, StringComparison.OrdinalIgnoreCase))
            return;

        float waitSeconds = Mathf.Max(0f, initialDissolveDuration) + Scene7DelayedDissolveExtraDelay;
        _scene7DelayedDissolveRoutine = StartCoroutine(Scene7DelayedDissolveRoutine(cfg.root, _scene7DelayedDissolveTarget, referencePoint, settings, waitSeconds));
    }

    private IEnumerator Scene7DelayedDissolveRoutine(GameObject sceneRoot, GameObject target, Transform referencePoint, SceneActivationUrpDissolveVfx.Settings settings, float waitSeconds)
    {
        if (waitSeconds > 0f)
            yield return new WaitForSeconds(waitSeconds);

        if (sceneRoot == null || target == null || !sceneRoot.activeInHierarchy)
        {
            _scene7DelayedDissolveRoutine = null;
            yield break;
        }

        target.SetActive(true);
        _sceneSpawnDissolveVfx.Play(target, referencePoint, settings);
        _scene7DelayedDissolveRoutine = null;
    }

    private void StopScene7DelayedDissolve(bool restoreTarget)
    {
        if (_scene7DelayedDissolveRoutine != null)
        {
            StopCoroutine(_scene7DelayedDissolveRoutine);
            _scene7DelayedDissolveRoutine = null;
        }

        if (restoreTarget && _scene7DelayedDissolveTarget != null)
            _scene7DelayedDissolveTarget.SetActive(_scene7DelayedDissolveTargetOriginalActive);

        _scene7DelayedDissolveTarget = null;
        _scene7DelayedDissolveTargetOriginalActive = false;
    }

    private void ApplySceneSpecificVisibility(VirtualScene cfg)
    {
        if (cfg == null || cfg.root == null)
            return;

        string activeSceneName = string.IsNullOrWhiteSpace(cfg.name) ? cfg.root.name : cfg.name;
        bool shouldForceOff = IsRuneOffRuleScene(activeSceneName);
        _isScene1RuleActive = shouldForceOff;

        if (enforceGlobalObjectByScene)
            ApplyGlobalObjectRule(shouldForceOff);

        if (enforceSkyboxByScene)
            ApplySkyboxRule(activeSceneName);

        ApplyScene1PlasticoHeight(cfg);
    }

    private void ApplyScene1PlasticoHeight(VirtualScene cfg)
    {
        if (!adjustScene1PlasticoHeight)
            return;

        bool shouldRaise = IsScene1Config(cfg);
        float verticalOffset = shouldRaise ? Mathf.Max(0f, scene1PlasticoHeightOffset) : 0f;
        List<GameObject> plasticoMatches = FindSceneObjectsByExactName(ProtectedScene1ObjectName);

        for (int i = 0; i < plasticoMatches.Count; i++)
        {
            Transform plastico = plasticoMatches[i] != null ? plasticoMatches[i].transform : null;
            if (plastico == null)
                continue;

            if (!_scene1PlasticoBaseLocalPositions.ContainsKey(plastico))
                _scene1PlasticoBaseLocalPositions[plastico] = plastico.localPosition;

            Vector3 baseLocalPosition = _scene1PlasticoBaseLocalPositions[plastico];
            Vector3 targetLocalPosition = baseLocalPosition + (Vector3.up * verticalOffset);
            if ((plastico.localPosition - targetLocalPosition).sqrMagnitude <= 0.000001f)
                continue;

            plastico.localPosition = targetLocalPosition;

            if (verboseLogs)
            {
                Debug.Log(
                    $"[SceneGroupManager] PlasticoMappa Y -> {plastico.localPosition.y:F3} " +
                    $"(scene={(cfg.root != null ? cfg.root.name : "<null>")}, raised={shouldRaise})");
            }
        }

        if (shouldRaise && verboseLogs && plasticoMatches.Count == 0)
            Debug.LogWarning("[SceneGroupManager] PlasticoMappa non trovato per il rialzo di Scena1.");
    }

    private void ApplyGlobalObjectRule(bool shouldForceOff)
    {
        bool shouldBeActive = !shouldForceOff;
        int totalMatches = 0;

        for (int nameIndex = 0; nameIndex < ForcedToggleObjectNames.Length; nameIndex++)
        {
            string exactName = ForcedToggleObjectNames[nameIndex];
            List<GameObject> matches = FindSceneObjectsByExactName(exactName);

            for (int i = 0; i < matches.Count; i++)
            {
                GameObject go = matches[i];
                if (go == null || IsUnderConfiguredSceneRoot(go.transform))
                    continue;

                totalMatches++;

                if (go.activeSelf == shouldBeActive)
                    continue;

                go.SetActive(shouldBeActive);

                if (verboseLogs)
                {
                    Debug.Log($"[SceneGroupManager] GlobalObject '{go.name}' -> {(shouldBeActive ? "ON" : "OFF")} (forcedOff={shouldForceOff})");
                }
            }
        }

        if (verboseLogs && totalMatches == 0)
            Debug.LogWarning($"[SceneGroupManager] Nessun GO trovato per i toggle globali: {string.Join(", ", ForcedToggleObjectNames)}.");

        // Protezione esplicita: solo in Scena1 il plastico deve restare acceso.
        if (IsScene1ActuallyActive())
        {
            List<GameObject> plasticoMatches = FindSceneObjectsByExactName(ProtectedScene1ObjectName);
            for (int i = 0; i < plasticoMatches.Count; i++)
            {
                GameObject go = plasticoMatches[i];
                if (go != null && !go.activeSelf)
                {
                    go.SetActive(true);
                    if (verboseLogs)
                        Debug.Log($"[SceneGroupManager] ProtectedObject '{go.name}' -> ON (scene1=true)");
                }
            }
        }
    }

    private bool IsRuneOffRuleCurrentlyActive()
    {
        if (scenes == null || scenes.Count == 0)
            return _isScene1RuleActive;

        for (int i = 0; i < scenes.Count; i++)
        {
            VirtualScene s = scenes[i];
            if (s == null || s.root == null)
                continue;

            if (s.root.activeInHierarchy)
            {
                string activeName = string.IsNullOrWhiteSpace(s.name) ? s.root.name : s.name;
                return IsRuneOffRuleScene(activeName);
            }
        }

        return _isScene1RuleActive;
    }

    private static bool IsRuneOffRuleScene(string sceneName)
    {
        return string.Equals(sceneName, ForcedScene1Name, StringComparison.OrdinalIgnoreCase)
               || string.Equals(sceneName, ForcedScene9Name, StringComparison.OrdinalIgnoreCase)
               || string.Equals(sceneName, ForcedScene10Name, StringComparison.OrdinalIgnoreCase);
    }

    private void ApplySkyboxRuleForCurrentActiveScene()
    {
        if (scenes == null || scenes.Count == 0)
            return;

        for (int i = 0; i < scenes.Count; i++)
        {
            VirtualScene s = scenes[i];
            if (s == null || s.root == null || !s.root.activeInHierarchy)
                continue;

            string activeName = string.IsNullOrWhiteSpace(s.name) ? s.root.name : s.name;
            ApplySkyboxRule(activeName);
            return;
        }
    }

    private void ApplySkyboxRule(string activeSceneName)
    {
        bool hideSkybox = IsRuneOffRuleScene(activeSceneName);
        Material desiredSkybox = hideSkybox ? null : ResolveDesiredSkyboxMaterial();
        if (!hideSkybox && Application.isMobilePlatform && forceRuntimeSkyDomeOnMobile)
            desiredSkybox = null;

        if (RenderSettings.skybox != desiredSkybox)
        {
            RenderSettings.skybox = desiredSkybox;
            DynamicGI.UpdateEnvironment();

            if (verboseLogs)
                Debug.Log($"[SceneGroupManager] Skybox -> {(hideSkybox ? "OFF" : "ON")} (scene={activeSceneName})");
        }

        ApplyRuntimeSkyDomeRule(hideSkybox);
        ApplyCameraNoSkyRule(hideSkybox);
    }

    private Material ResolveDesiredSkyboxMaterial()
    {
        if (_runtimeSkyboxMaterial != null)
        {
            SyncRuntimeSkyboxMaterial(_runtimeSkyboxMaterial);
            return _runtimeSkyboxMaterial;
        }

        if (preferSkyboxTextureResourceOverAssignedMaterial)
        {
            Material preferredFromResource = TryCreateRuntimeSkyboxMaterialFromResource();
            if (preferredFromResource != null)
                return preferredFromResource;
        }

        if (globalSkyboxMaterial != null && IsUsableSkyboxMaterial(globalSkyboxMaterial))
            return globalSkyboxMaterial;

        Material fallbackFromResource = TryCreateRuntimeSkyboxMaterialFromResource();
        if (fallbackFromResource != null)
            return fallbackFromResource;

        if (globalSkyboxMaterial != null)
            return globalSkyboxMaterial;

        return _initialSkyboxMaterial;
    }

    private Material TryCreateRuntimeSkyboxMaterialFromResource()
    {
        Texture skyTexture = TryLoadSkyTextureFromResources();
        if (skyTexture == null)
            return null;

        string requiredShaderName = GetSkyboxShaderNameForTexture(skyTexture);
        if (string.IsNullOrEmpty(requiredShaderName))
            return null;

        if (_runtimeSkyboxMaterial != null)
        {
            string currentShaderName = _runtimeSkyboxMaterial.shader != null ? _runtimeSkyboxMaterial.shader.name : string.Empty;
            if (!string.Equals(currentShaderName, requiredShaderName, StringComparison.OrdinalIgnoreCase))
            {
                Destroy(_runtimeSkyboxMaterial);
                _runtimeSkyboxMaterial = null;
            }
        }

        if (_runtimeSkyboxMaterial == null)
        {
            Shader skyboxShader = Shader.Find(requiredShaderName);
            if (skyboxShader == null)
                return null;

            _runtimeSkyboxMaterial = new Material(skyboxShader)
            {
                name = "Runtime_GlobalSkybox_" + globalSkyboxTextureResourceName,
                hideFlags = HideFlags.DontSave
            };
        }

        SyncRuntimeSkyboxMaterial(_runtimeSkyboxMaterial, skyTexture);
        return _runtimeSkyboxMaterial;
    }

    private static string GetSkyboxShaderNameForTexture(Texture skyTexture)
    {
        if (skyTexture == null)
            return null;

        return skyTexture is Cubemap ? "Skybox/Cubemap" : "Skybox/Panoramic";
    }

    private void SyncRuntimeSkyboxMaterial(Material material)
    {
        if (material == null)
            return;

        Texture skyTexture = TryLoadSkyTextureFromResources();
        if (skyTexture == null)
            return;

        SyncRuntimeSkyboxMaterial(material, skyTexture);
    }

    private void SyncRuntimeSkyboxMaterial(Material material, Texture skyTexture)
    {
        if (material == null || skyTexture == null)
            return;

        bool isCube = skyTexture is Cubemap;

        if (material.HasProperty("_MainTex"))
            material.SetTexture("_MainTex", isCube ? null : skyTexture);
        if (material.HasProperty("_Tex"))
            material.SetTexture("_Tex", isCube ? skyTexture : null);
        if (material.HasProperty("_Tint"))
            material.SetColor("_Tint", Color.white);
        if (material.HasProperty("_Exposure"))
            material.SetFloat("_Exposure", GetEffectiveSkyExposure());
        if (material.HasProperty("_Rotation"))
            material.SetFloat("_Rotation", globalSkyboxRotation);
        if (material.HasProperty("_Mapping"))
            material.SetFloat("_Mapping", 1f);
        if (material.HasProperty("_ImageType"))
            material.SetFloat("_ImageType", isCube ? 1f : 0f);
    }

    private Texture TryLoadSkyTextureFromResources()
    {
        if (_runtimeSkyTexture != null)
            return _runtimeSkyTexture;

        if (!string.IsNullOrWhiteSpace(globalSkyboxTextureResourceName))
            _runtimeSkyTexture = Resources.Load<Texture>(globalSkyboxTextureResourceName);

        if (_runtimeSkyTexture == null && verboseLogs && !_hasWarnedMissingSkyboxTexture)
        {
            Debug.LogWarning($"[SceneGroupManager] Skybox texture non trovata in Resources: '{globalSkyboxTextureResourceName}'.");
            _hasWarnedMissingSkyboxTexture = true;
        }

        return _runtimeSkyTexture;
    }

    private float GetEffectiveSkyExposure()
    {
        float exposure = Mathf.Max(0f, globalSkyboxExposure);
        if (Application.isMobilePlatform)
            exposure *= Mathf.Clamp(mobileSkyExposureMultiplier, 0.1f, 1f);

        return exposure;
    }

    private static bool IsUsableSkyboxMaterial(Material material)
    {
        if (material == null || material.shader == null)
            return false;

        string shaderName = material.shader.name;
        if (string.Equals(shaderName, "Skybox/Procedural", StringComparison.OrdinalIgnoreCase))
            return false;

        if (material.HasProperty("_MainTex") && material.GetTexture("_MainTex") != null)
            return true;

        if (material.HasProperty("_Tex") && material.GetTexture("_Tex") != null)
            return true;

        return !shaderName.Contains("Skybox/");
    }

    private void ApplyCameraNoSkyRule(bool hideSkybox)
    {
        Camera[] cameras = GetSceneCameras();
        Camera mainCamera = Camera.main;
        bool hasSkyboxMaterial = RenderSettings.skybox != null;
        bool hasRuntimeSkyDome = IsRuntimeSkyDomeVisible();
        bool hasVisibleSky = hasSkyboxMaterial || hasRuntimeSkyDome;
        if (hideSkybox)
        {
            for (int i = 0; i < cameras.Length; i++)
            {
                Camera cam = cameras[i];
                if (cam == null || !cam.gameObject.scene.IsValid())
                    continue;
                if (cam.targetTexture != null)
                    continue;
                if (cam.stereoTargetEye == StereoTargetEyeMask.None && cam != mainCamera && !cam.CompareTag("MainCamera"))
                    continue;

                int id = cam.GetInstanceID();
                if (!_cameraClearFlagsBackup.ContainsKey(id))
                {
                    _cameraClearFlagsBackup[id] = cam.clearFlags;
                    _cameraBackgroundBackup[id] = cam.backgroundColor;
                }

                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = scene1NoSkyColor;
            }

            return;
        }

        for (int i = 0; i < cameras.Length; i++)
        {
            Camera cam = cameras[i];
            if (cam == null || !cam.gameObject.scene.IsValid())
                continue;
            if (cam.targetTexture != null)
                continue;
            if (cam.stereoTargetEye == StereoTargetEyeMask.None && cam != mainCamera && !cam.CompareTag("MainCamera"))
                continue;

            int id = cam.GetInstanceID();
            if (!_cameraClearFlagsBackup.ContainsKey(id))
            {
                _cameraClearFlagsBackup[id] = cam.clearFlags;
                _cameraBackgroundBackup[id] = cam.backgroundColor;
            }

            if (hasVisibleSky)
                cam.clearFlags = hasSkyboxMaterial ? CameraClearFlags.Skybox : CameraClearFlags.SolidColor;
            else
                cam.clearFlags = _cameraClearFlagsBackup[id];

            if (_cameraBackgroundBackup.TryGetValue(id, out Color color))
                cam.backgroundColor = color;
        }
    }

    private void ApplyRuntimeSkyDomeRule(bool hideSkybox)
    {
        if (_runtimeSkyDomeTransform == null && !enableRuntimeSkyDomeFallback)
            return;

        bool shouldShow = !hideSkybox && ShouldShowRuntimeSkyDome();
        if (!shouldShow)
        {
            SetRuntimeSkyDomeVisible(false);
            return;
        }

        if (!EnsureRuntimeSkyDome())
        {
            SetRuntimeSkyDomeVisible(false);
            return;
        }

        UpdateRuntimeSkyDomePose();
        SetRuntimeSkyDomeVisible(true);
    }

    private bool ShouldShowRuntimeSkyDome()
    {
        if (!enableRuntimeSkyDomeFallback)
            return false;

        if (forceRuntimeSkyDomeOnMobile && Application.isMobilePlatform)
            return true;

        return RenderSettings.skybox == null;
    }

    private Camera[] GetSceneCameras()
    {
        if (_cachedSceneCameras.Length == 0
            || Time.unscaledTime >= _nextCameraCacheRefreshTime
            || HasInvalidCachedCameraReference())
        {
            _cachedSceneCameras = FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            _nextCameraCacheRefreshTime = Time.unscaledTime + CameraCacheRefreshInterval;
        }

        return _cachedSceneCameras;
    }

    private bool HasInvalidCachedCameraReference()
    {
        for (int i = 0; i < _cachedSceneCameras.Length; i++)
        {
            Camera cam = _cachedSceneCameras[i];
            if (cam == null || !cam.gameObject.scene.IsValid())
                return true;
        }

        return false;
    }

    private bool EnsureRuntimeSkyDome()
    {
        Texture skyTexture = TryLoadSkyTextureFromResources();
        if (skyTexture == null)
            return false;

        if (_runtimeSkyDomeTransform == null)
            CreateRuntimeSkyDomeObject();

        if (_runtimeSkyDomeTransform == null || _runtimeSkyDomeRenderer == null)
            return false;

        if (_runtimeSkyDomeMaterial == null)
        {
            Shader skyDomeShader = Resources.Load<Shader>(runtimeSkyDomeShaderResourceName);
            if (skyDomeShader == null)
            {
                if (verboseLogs && !_hasWarnedMissingSkyDomeShader)
                {
                    Debug.LogWarning($"[SceneGroupManager] Shader sky dome non trovato in Resources: '{runtimeSkyDomeShaderResourceName}'.");
                    _hasWarnedMissingSkyDomeShader = true;
                }

                return false;
            }

            _runtimeSkyDomeMaterial = new Material(skyDomeShader)
            {
                name = "Runtime_SkyDome_" + globalSkyboxTextureResourceName,
                hideFlags = HideFlags.DontSave
            };
            _runtimeSkyDomeRenderer.sharedMaterial = _runtimeSkyDomeMaterial;
        }

        SyncRuntimeSkyDomeMaterial(skyTexture);
        return true;
    }

    private void CreateRuntimeSkyDomeObject()
    {
        GameObject dome = new GameObject("RuntimeSkyDome", typeof(MeshFilter), typeof(MeshRenderer));
        dome.name = "RuntimeSkyDome";
        dome.layer = 2;

        _runtimeSkyDomeTransform = dome.transform;
        MeshFilter domeFilter = dome.GetComponent<MeshFilter>();
        _runtimeSkyDomeRenderer = dome.GetComponent<MeshRenderer>();
        if (_runtimeSkyDomeRenderer == null || domeFilter == null)
            return;

        if (_runtimeSkyDomeMesh == null)
        {
            _runtimeSkyDomeMesh = BuildRuntimeSkyDomeMesh(RuntimeSkyDomeLongitudeSegments, RuntimeSkyDomeLatitudeSegments);
            if (_runtimeSkyDomeMesh != null)
                _runtimeSkyDomeMesh.hideFlags = HideFlags.DontSave;
        }

        domeFilter.sharedMesh = _runtimeSkyDomeMesh;
        _runtimeSkyDomeRenderer.shadowCastingMode = ShadowCastingMode.Off;
        _runtimeSkyDomeRenderer.receiveShadows = false;
        _runtimeSkyDomeRenderer.lightProbeUsage = LightProbeUsage.Off;
        _runtimeSkyDomeRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        _runtimeSkyDomeRenderer.allowOcclusionWhenDynamic = false;
    }

    private void SyncRuntimeSkyDomeMaterial(Texture skyTexture)
    {
        if (_runtimeSkyDomeMaterial == null || _runtimeSkyDomeTransform == null)
            return;

        if (_runtimeSkyDomeMaterial.HasProperty("_Tint"))
            _runtimeSkyDomeMaterial.SetColor("_Tint", Color.white);
        if (_runtimeSkyDomeMaterial.HasProperty("_Exposure"))
        {
            float domeExposure = GetEffectiveSkyExposure() * Mathf.Max(0.05f, runtimeSkyDomeExposureMultiplier);
            if (Application.isMobilePlatform)
                domeExposure = Mathf.Min(domeExposure, Mathf.Max(0.05f, mobileRuntimeSkyDomeExposureCap));

            _runtimeSkyDomeMaterial.SetFloat("_Exposure", domeExposure);
        }
        if (_runtimeSkyDomeMaterial.HasProperty("_PanoramaMipLevel"))
            _runtimeSkyDomeMaterial.SetFloat("_PanoramaMipLevel", 0f);
        bool isCube = skyTexture is Cubemap;
        if (_runtimeSkyDomeMaterial.HasProperty("_UseCube"))
            _runtimeSkyDomeMaterial.SetFloat("_UseCube", isCube ? 1f : 0f);

        if (isCube)
        {
            if (_runtimeSkyDomeMaterial.HasProperty("_CubeTex"))
                _runtimeSkyDomeMaterial.SetTexture("_CubeTex", skyTexture);
            if (_runtimeSkyDomeMaterial.HasProperty("_MainTex"))
                _runtimeSkyDomeMaterial.SetTexture("_MainTex", null);
        }
        else
        {
            if (_runtimeSkyDomeMaterial.HasProperty("_MainTex"))
                _runtimeSkyDomeMaterial.SetTexture("_MainTex", skyTexture);
            if (_runtimeSkyDomeMaterial.HasProperty("_CubeTex"))
                _runtimeSkyDomeMaterial.SetTexture("_CubeTex", null);
        }

        _runtimeSkyDomeTransform.localScale = Vector3.one * Mathf.Max(20f, runtimeSkyDomeRadius * 2f);
    }

    private void UpdateRuntimeSkyDomePose()
    {
        if (_runtimeSkyDomeTransform == null)
            return;

        Transform reference = Camera.main != null ? Camera.main.transform : player;
        if (reference == null)
            return;

        _runtimeSkyDomeTransform.position = reference.position;
        _runtimeSkyDomeTransform.rotation =
            Quaternion.Euler(runtimeSkyDomeEulerOffset.x, globalSkyboxRotation + runtimeSkyDomeEulerOffset.y, runtimeSkyDomeEulerOffset.z);
    }

    private void SetRuntimeSkyDomeVisible(bool visible)
    {
        if (_runtimeSkyDomeRenderer == null)
            return;

        _runtimeSkyDomeRenderer.enabled = visible;
    }

    private bool IsRuntimeSkyDomeVisible()
    {
        return _runtimeSkyDomeRenderer != null && _runtimeSkyDomeRenderer.enabled;
    }

    private void DestroyRuntimeSkyDome()
    {
        if (_runtimeSkyDomeTransform != null)
        {
            Destroy(_runtimeSkyDomeTransform.gameObject);
            _runtimeSkyDomeTransform = null;
            _runtimeSkyDomeRenderer = null;
        }

        if (_runtimeSkyDomeMaterial != null)
        {
            Destroy(_runtimeSkyDomeMaterial);
            _runtimeSkyDomeMaterial = null;
        }

        if (_runtimeSkyDomeMesh != null)
        {
            Destroy(_runtimeSkyDomeMesh);
            _runtimeSkyDomeMesh = null;
        }
    }

    private static Mesh BuildRuntimeSkyDomeMesh(int longitudeSegments, int latitudeSegments)
    {
        longitudeSegments = Mathf.Clamp(longitudeSegments, 16, 256);
        latitudeSegments = Mathf.Clamp(latitudeSegments, 8, 128);

        int ringCount = latitudeSegments - 1;
        int vertexCount = 2 + (ringCount * longitudeSegments);
        int triangleCount = longitudeSegments * 2 + ((latitudeSegments - 2) * longitudeSegments * 2);

        Vector3[] vertices = new Vector3[vertexCount];
        int[] triangles = new int[triangleCount * 3];

        vertices[0] = Vector3.up;
        int vertexIndex = 1;

        for (int lat = 1; lat < latitudeSegments; lat++)
        {
            float v = lat / (float)latitudeSegments;
            float phi = Mathf.PI * v;
            float sinPhi = Mathf.Sin(phi);
            float cosPhi = Mathf.Cos(phi);

            for (int lon = 0; lon < longitudeSegments; lon++)
            {
                float u = lon / (float)longitudeSegments;
                float theta = u * Mathf.PI * 2f;
                float sinTheta = Mathf.Sin(theta);
                float cosTheta = Mathf.Cos(theta);
                vertices[vertexIndex++] = new Vector3(sinPhi * cosTheta, cosPhi, sinPhi * sinTheta);
            }
        }

        vertices[vertexIndex] = Vector3.down;

        int triangleIndex = 0;
        int southPoleIndex = vertexCount - 1;

        for (int lon = 0; lon < longitudeSegments; lon++)
        {
            int nextLon = (lon + 1) % longitudeSegments;
            triangles[triangleIndex++] = 0;
            triangles[triangleIndex++] = 1 + nextLon;
            triangles[triangleIndex++] = 1 + lon;
        }

        for (int lat = 0; lat < latitudeSegments - 2; lat++)
        {
            int currentRingStart = 1 + (lat * longitudeSegments);
            int nextRingStart = currentRingStart + longitudeSegments;

            for (int lon = 0; lon < longitudeSegments; lon++)
            {
                int nextLon = (lon + 1) % longitudeSegments;
                int current = currentRingStart + lon;
                int currentNext = currentRingStart + nextLon;
                int below = nextRingStart + lon;
                int belowNext = nextRingStart + nextLon;

                triangles[triangleIndex++] = current;
                triangles[triangleIndex++] = currentNext;
                triangles[triangleIndex++] = belowNext;

                triangles[triangleIndex++] = current;
                triangles[triangleIndex++] = belowNext;
                triangles[triangleIndex++] = below;
            }
        }

        int lastRingStart = southPoleIndex - longitudeSegments;
        for (int lon = 0; lon < longitudeSegments; lon++)
        {
            int nextLon = (lon + 1) % longitudeSegments;
            triangles[triangleIndex++] = southPoleIndex;
            triangles[triangleIndex++] = lastRingStart + lon;
            triangles[triangleIndex++] = lastRingStart + nextLon;
        }

        Mesh mesh = new Mesh
        {
            name = "RuntimeSkyDomeSphere"
        };

        if (vertexCount > 65535)
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;

        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    private void RestoreSkyboxAndCameraDefaults()
    {
        if (_cameraClearFlagsBackup.Count > 0 || _cameraBackgroundBackup.Count > 0)
        {
            Camera[] cameras = FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < cameras.Length; i++)
            {
                Camera cam = cameras[i];
                if (cam == null || !cam.gameObject.scene.IsValid())
                    continue;

                int id = cam.GetInstanceID();
                if (_cameraClearFlagsBackup.TryGetValue(id, out CameraClearFlags flags))
                    cam.clearFlags = flags;
                if (_cameraBackgroundBackup.TryGetValue(id, out Color color))
                    cam.backgroundColor = color;
            }

            _cameraClearFlagsBackup.Clear();
            _cameraBackgroundBackup.Clear();
        }

        if (_hasCapturedInitialSkybox && RenderSettings.skybox != _initialSkyboxMaterial)
        {
            RenderSettings.skybox = _initialSkyboxMaterial;
            DynamicGI.UpdateEnvironment();
        }

        SetRuntimeSkyDomeVisible(false);
    }

    private bool IsScene1ActuallyActive()
    {
        if (scenes == null || scenes.Count == 0)
            return false;

        for (int i = 0; i < scenes.Count; i++)
        {
            VirtualScene s = scenes[i];
            if (s == null || s.root == null || !s.root.activeInHierarchy)
                continue;

            string activeName = string.IsNullOrWhiteSpace(s.name) ? s.root.name : s.name;
            return string.Equals(activeName, ForcedScene1Name, StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    private bool IsUnderConfiguredSceneRoot(Transform candidate)
    {
        if (candidate == null || scenes == null || scenes.Count == 0)
            return false;

        for (int i = 0; i < scenes.Count; i++)
        {
            VirtualScene s = scenes[i];
            if (s == null || s.root == null)
                continue;

            Transform root = s.root.transform;
            if (candidate == root || candidate.IsChildOf(root))
                return true;
        }

        return false;
    }

    private List<GameObject> FindSceneObjectsByExactName(string exactName)
    {
        if (string.IsNullOrWhiteSpace(exactName))
            return new List<GameObject>(0);

        if (_sceneObjectsByExactNameCache.TryGetValue(exactName, out List<GameObject> cached))
        {
            PruneInvalidSceneObjectReferences(cached);
            if (cached.Count > 0)
                return cached;

            _sceneObjectsByExactNameCache.Remove(exactName);
        }

        var result = new List<GameObject>(4);

        int sceneCount = SceneManager.sceneCount;
        for (int s = 0; s < sceneCount; s++)
        {
            Scene scene = SceneManager.GetSceneAt(s);
            if (!scene.IsValid() || !scene.isLoaded)
                continue;

            var roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                CollectExactMatchesRecursive(roots[i].transform, exactName, result);
            }
        }

        _sceneObjectsByExactNameCache[exactName] = result;
        return result;
    }

    private static void PruneInvalidSceneObjectReferences(List<GameObject> objects)
    {
        if (objects == null)
            return;

        for (int i = objects.Count - 1; i >= 0; i--)
        {
            GameObject go = objects[i];
            if (go == null || !go.scene.IsValid())
                objects.RemoveAt(i);
        }
    }

    private void TryStartEpigrafeScene3Entrance(VirtualScene cfg)
    {
        if (!animateEpigrafeOnScene3 || cfg == null || cfg.root == null)
            return;

        string activeSceneName = string.IsNullOrWhiteSpace(cfg.name) ? cfg.root.name : cfg.name;
        if (!string.Equals(activeSceneName, scene3Name, StringComparison.OrdinalIgnoreCase))
            return;

        Transform epigrafe = FindChildByNameContains(cfg.root.transform, epigrafeNameToken);
        if (epigrafe == null)
        {
            if (verboseLogs)
                Debug.LogWarning($"[SceneGroupManager] Scena3 attiva ma Epigrafe non trovata (token='{epigrafeNameToken}').");
            return;
        }

        if (!_epigrafeBaseLocalPositions.ContainsKey(epigrafe))
            _epigrafeBaseLocalPositions[epigrafe] = epigrafe.localPosition;
        if (!_epigrafeBaseLocalScales.ContainsKey(epigrafe))
            _epigrafeBaseLocalScales[epigrafe] = epigrafe.localScale;

        Vector3 baseLocalPos = _epigrafeBaseLocalPositions[epigrafe];
        Vector3 baseLocalScale = _epigrafeBaseLocalScales[epigrafe];

        epigrafe.localPosition = baseLocalPos;
        epigrafe.localScale = baseLocalScale;

        EpigrafeGlowController glow = EnsureEpigrafeGlowController(epigrafe);
        _epigrafeEntranceRoutine = StartCoroutine(EpigrafeEntranceRoutine(epigrafe, baseLocalScale, glow));
    }

    private IEnumerator EpigrafeEntranceRoutine(Transform epigrafe, Vector3 baseLocalScale, EpigrafeGlowController glow)
    {
        if (epigrafe == null)
            yield break;

        float delay = Mathf.Max(0f, epigrafeEntranceDelay);
        if (glow != null && delay > 0f)
            glow.PlayPreEntranceSparkle(delay);

        if (delay > 0f)
            yield return new WaitForSeconds(delay);

        float duration = Mathf.Max(0.01f, epigrafeEntranceDuration);
        Vector3 startWorld = epigrafe.position;
        Vector3 targetScale = baseLocalScale * Mathf.Max(0.1f, epigrafeScaleMultiplier);

        Vector3 fallbackForward = epigrafe.forward;
        fallbackForward.y = 0f;
        if (fallbackForward.sqrMagnitude <= 0.0001f)
            fallbackForward = Vector3.forward;

        Vector3 targetWorld = startWorld + fallbackForward.normalized * Mathf.Max(0f, epigrafeMoveTowardsPlayerDistance);
        Transform viewer = TryGetPlayerHeadTransform();
        if (viewer == null)
            viewer = player;

        if (viewer != null)
        {
            Vector3 viewerTarget = viewer.position;
            viewerTarget.y = startWorld.y;

            Vector3 toViewer = viewerTarget - startWorld;
            if (toViewer.sqrMagnitude > 0.0001f)
            {
                float moveFraction = Mathf.Clamp01(epigrafeMoveTowardsPlayerFraction);
                targetWorld = Vector3.Lerp(startWorld, viewerTarget, moveFraction);
            }
        }

        float elapsed = 0f;
        while (elapsed < duration)
        {
            if (epigrafe == null)
                yield break;

            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float eased = t * t * (3f - 2f * t); // smoothstep

            epigrafe.position = Vector3.Lerp(startWorld, targetWorld, eased);
            epigrafe.localScale = Vector3.Lerp(baseLocalScale, targetScale, eased);
            yield return null;
        }

        if (epigrafe != null)
        {
            epigrafe.position = targetWorld;
            epigrafe.localScale = targetScale;
        }

        _epigrafeEntranceRoutine = null;
    }

    private static EpigrafeGlowController EnsureEpigrafeGlowController(Transform epigrafe)
    {
        if (epigrafe == null || epigrafe.GetComponent<Renderer>() == null)
            return null;

        EpigrafeGlowController glow = epigrafe.GetComponent<EpigrafeGlowController>();
        if (glow == null)
            glow = epigrafe.gameObject.AddComponent<EpigrafeGlowController>();

        return glow;
    }

    private void StopEpigrafeEntrance()
    {
        if (_epigrafeEntranceRoutine == null)
            return;

        StopCoroutine(_epigrafeEntranceRoutine);
        _epigrafeEntranceRoutine = null;
    }

    private static Transform FindChildByNameContains(Transform root, string token)
    {
        if (root == null || string.IsNullOrWhiteSpace(token))
            return null;

        string tokenTrimmed = token.Trim();
        return FindChildByNameContainsRecursive(root, tokenTrimmed);
    }

    private static Transform FindChildByExactName(Transform root, string exactName)
    {
        if (root == null || string.IsNullOrWhiteSpace(exactName))
            return null;

        if (IsNameMatch(root.name, exactName))
            return root;

        for (int i = 0; i < root.childCount; i++)
        {
            Transform found = FindChildByExactName(root.GetChild(i), exactName);
            if (found != null)
                return found;
        }

        return null;
    }

    private static Transform FindChildByNameContainsRecursive(Transform current, string token)
    {
        if (current == null)
            return null;

        if (!string.IsNullOrEmpty(current.name) &&
            current.name.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return current;
        }

        for (int i = 0; i < current.childCount; i++)
        {
            Transform found = FindChildByNameContainsRecursive(current.GetChild(i), token);
            if (found != null)
                return found;
        }

        return null;
    }

    private static void CollectExactMatchesRecursive(Transform t, string exactName, List<GameObject> result)
    {
        if (t == null)
            return;

        if (IsNameMatch(t.name, exactName))
            result.Add(t.gameObject);

        for (int i = 0; i < t.childCount; i++)
            CollectExactMatchesRecursive(t.GetChild(i), exactName, result);
    }

    private static bool IsNameMatch(string currentName, string targetName)
    {
        if (string.IsNullOrWhiteSpace(currentName) || string.IsNullOrWhiteSpace(targetName))
            return false;

        if (string.Equals(currentName, targetName, StringComparison.Ordinal))
            return true;

        return currentName.StartsWith(targetName + " (", StringComparison.Ordinal);
    }

    private void TryStartScene8ChurchDelayedFade(VirtualScene cfg)
    {
        if (!enableScene8ChurchDelayedFade || cfg == null || cfg.root == null)
            return;

        string sceneName = string.IsNullOrWhiteSpace(cfg.name) ? cfg.root.name : cfg.name;
        if (!string.Equals(sceneName, scene8Name, StringComparison.OrdinalIgnoreCase))
            return;

        List<GameObject> churchTargets = new List<GameObject>();
        CollectExactMatchesRecursive(cfg.root.transform, scene8ChurchObjectName, churchTargets);
        if (churchTargets.Count == 0)
        {
            if (verboseLogs)
            {
                Debug.LogWarning(
                    $"[SceneGroupManager] Nessun target '{scene8ChurchObjectName}' trovato in {sceneName} per il fade ritardato.");
            }
            return;
        }

        CacheScene8ChurchState(churchTargets);
        _scene8ChurchFadeRoutine = StartCoroutine(Scene8ChurchDelayedFadeRoutine());
    }

    private IEnumerator Scene8ChurchDelayedFadeRoutine()
    {
        HideScene8ChurchTargets();

        float delay = Mathf.Max(0f, scene8ChurchDelaySeconds);
        if (delay > 0f)
            yield return new WaitForSeconds(delay);

        ShowScene8ChurchTargets();
        PrepareScene8ChurchFadeState();
        ApplyScene8ChurchFade(0f);

        float duration = Mathf.Max(0.01f, scene8ChurchFadeDuration);
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float eased = t * t * (3f - 2f * t);
            ApplyScene8ChurchFade(eased);
            yield return null;
        }

        ApplyScene8ChurchFade(1f);
        RestoreScene8ChurchRendererMaterials();
        _scene8ChurchFadeRoutine = null;
    }

    private void CacheScene8ChurchState(List<GameObject> churchTargets)
    {
        ClearScene8ChurchState();

        for (int i = 0; i < churchTargets.Count; i++)
        {
            GameObject target = churchTargets[i];
            if (target == null)
                continue;

            _scene8ChurchTargetStates.Add(new Scene8ChurchTargetState
            {
                Target = target,
                OriginalActive = target.activeSelf,
            });

            Renderer[] renderers = target.GetComponentsInChildren<Renderer>(true);
            for (int rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
            {
                Renderer renderer = renderers[rendererIndex];
                if (renderer == null)
                    continue;

                _scene8ChurchRendererStates.Add(new Scene8RendererFadeState
                {
                    Renderer = renderer,
                    OriginalSharedMaterials = renderer.sharedMaterials,
                });
            }

            Light[] lights = target.GetComponentsInChildren<Light>(true);
            for (int lightIndex = 0; lightIndex < lights.Length; lightIndex++)
            {
                Light sceneLight = lights[lightIndex];
                if (sceneLight == null)
                    continue;

                _scene8ChurchLightStates.Add(new Scene8LightFadeState
                {
                    Light = sceneLight,
                    OriginalIntensity = sceneLight.intensity,
                });
            }
        }
    }

    private void HideScene8ChurchTargets()
    {
        for (int i = 0; i < _scene8ChurchTargetStates.Count; i++)
        {
            Scene8ChurchTargetState targetState = _scene8ChurchTargetStates[i];
            if (targetState?.Target == null)
                continue;

            targetState.Target.SetActive(false);
        }
    }

    private void ShowScene8ChurchTargets()
    {
        for (int i = 0; i < _scene8ChurchTargetStates.Count; i++)
        {
            Scene8ChurchTargetState targetState = _scene8ChurchTargetStates[i];
            if (targetState?.Target == null)
                continue;

            targetState.Target.SetActive(true);
        }
    }

    private void PrepareScene8ChurchFadeState()
    {
        for (int i = 0; i < _scene8ChurchRendererStates.Count; i++)
        {
            Scene8RendererFadeState rendererState = _scene8ChurchRendererStates[i];
            if (rendererState == null || rendererState.Renderer == null)
                continue;

            Material[] originalSharedMaterials = rendererState.OriginalSharedMaterials;
            Material[] runtimeMaterials = new Material[originalSharedMaterials.Length];
            for (int materialIndex = 0; materialIndex < originalSharedMaterials.Length; materialIndex++)
            {
                Material originalMaterial = originalSharedMaterials[materialIndex];
                if (originalMaterial == null)
                    continue;

                Material runtimeMaterial = new Material(originalMaterial);
                PrepareMaterialForFade(runtimeMaterial);
                SetMaterialAlpha(runtimeMaterial, 0f);
                runtimeMaterials[materialIndex] = runtimeMaterial;
            }

            rendererState.RuntimeMaterials = runtimeMaterials;
            rendererState.Renderer.materials = runtimeMaterials;
        }

        for (int i = 0; i < _scene8ChurchLightStates.Count; i++)
        {
            Scene8LightFadeState lightState = _scene8ChurchLightStates[i];
            if (lightState?.Light == null)
                continue;

            lightState.Light.intensity = 0f;
        }
    }

    private void ApplyScene8ChurchFade(float alpha)
    {
        float clampedAlpha = Mathf.Clamp01(alpha);

        for (int i = 0; i < _scene8ChurchRendererStates.Count; i++)
        {
            Scene8RendererFadeState rendererState = _scene8ChurchRendererStates[i];
            if (rendererState?.RuntimeMaterials == null)
                continue;

            for (int materialIndex = 0; materialIndex < rendererState.RuntimeMaterials.Length; materialIndex++)
            {
                Material runtimeMaterial = rendererState.RuntimeMaterials[materialIndex];
                if (runtimeMaterial == null)
                    continue;

                SetMaterialAlpha(runtimeMaterial, clampedAlpha);
            }
        }

        for (int i = 0; i < _scene8ChurchLightStates.Count; i++)
        {
            Scene8LightFadeState lightState = _scene8ChurchLightStates[i];
            if (lightState?.Light == null)
                continue;

            lightState.Light.intensity = lightState.OriginalIntensity * clampedAlpha;
        }
    }

    private void StopScene8ChurchDelayedFade(bool restoreState)
    {
        if (_scene8ChurchFadeRoutine != null)
        {
            StopCoroutine(_scene8ChurchFadeRoutine);
            _scene8ChurchFadeRoutine = null;
        }

        if (restoreState)
            RestoreScene8ChurchState();
        else
            ClearScene8ChurchState();
    }

    private void RestoreScene8ChurchState()
    {
        RestoreScene8ChurchRendererMaterials();

        for (int i = 0; i < _scene8ChurchLightStates.Count; i++)
        {
            Scene8LightFadeState lightState = _scene8ChurchLightStates[i];
            if (lightState?.Light == null)
                continue;

            lightState.Light.intensity = lightState.OriginalIntensity;
        }

        for (int i = 0; i < _scene8ChurchTargetStates.Count; i++)
        {
            Scene8ChurchTargetState targetState = _scene8ChurchTargetStates[i];
            if (targetState?.Target == null)
                continue;

            targetState.Target.SetActive(targetState.OriginalActive);
        }

        ClearScene8ChurchState();
    }

    private void RestoreScene8ChurchRendererMaterials()
    {
        for (int i = 0; i < _scene8ChurchRendererStates.Count; i++)
        {
            Scene8RendererFadeState rendererState = _scene8ChurchRendererStates[i];
            if (rendererState == null)
                continue;

            if (rendererState.Renderer != null && rendererState.OriginalSharedMaterials != null)
                rendererState.Renderer.sharedMaterials = rendererState.OriginalSharedMaterials;

            DestroyScene8RuntimeMaterials(rendererState.RuntimeMaterials);
            rendererState.RuntimeMaterials = null;
        }
    }

    private void ClearScene8ChurchState()
    {
        _scene8ChurchTargetStates.Clear();
        _scene8ChurchRendererStates.Clear();
        _scene8ChurchLightStates.Clear();
    }

    private void DestroyScene8RuntimeMaterials(Material[] materials)
    {
        if (materials == null)
            return;

        for (int i = 0; i < materials.Length; i++)
        {
            Material material = materials[i];
            if (material != null)
                Destroy(material);
        }
    }

    private static void PrepareMaterialForFade(Material material)
    {
        if (material == null)
            return;

        string shaderName = material.shader != null ? material.shader.name : string.Empty;
        if (material.HasFloat("_Mode") && string.Equals(shaderName, "Standard", StringComparison.OrdinalIgnoreCase))
            material.SetFloat("_Mode", 2f);

        if (material.HasFloat("_Surface"))
            material.SetFloat("_Surface", 1f);
        if (material.HasFloat("_SurfaceType"))
            material.SetFloat("_SurfaceType", 1f);
        if (material.HasFloat("_Blend"))
            material.SetFloat("_Blend", 0f);
        if (material.HasFloat("_SrcBlend"))
            material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        if (material.HasFloat("_DstBlend"))
            material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        if (material.HasFloat("_ZWrite"))
            material.SetFloat("_ZWrite", 0f);
        if (material.HasFloat("_AlphaClip"))
            material.SetFloat("_AlphaClip", 0f);

        material.DisableKeyword("_ALPHATEST_ON");
        material.EnableKeyword("_ALPHABLEND_ON");
        material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        material.renderQueue = (int)RenderQueue.Transparent;
    }

    private static void SetMaterialAlpha(Material material, float alpha)
    {
        if (material == null)
            return;

        float clampedAlpha = Mathf.Clamp01(alpha);

        if (material.HasProperty("_Color"))
        {
            Color color = material.GetColor("_Color");
            color.a = clampedAlpha;
            material.SetColor("_Color", color);
        }

        if (material.HasProperty("_BaseColor"))
        {
            Color baseColor = material.GetColor("_BaseColor");
            baseColor.a = clampedAlpha;
            material.SetColor("_BaseColor", baseColor);
        }

        if (material.HasProperty("_TintColor"))
        {
            Color tintColor = material.GetColor("_TintColor");
            tintColor.a = clampedAlpha;
            material.SetColor("_TintColor", tintColor);
        }
    }

    private void EnsureScene6StaticTorchFlames(VirtualScene cfg)
    {
        if (!enableScene6StaticTorchFlames || cfg == null || cfg.root == null)
            return;

        string sceneName = string.IsNullOrWhiteSpace(cfg.name) ? cfg.root.name : cfg.name;
        if (!string.Equals(sceneName, scene6TorchSceneName, StringComparison.OrdinalIgnoreCase))
            return;

        List<GameObject> torchRoots = new List<GameObject>(8);
        CollectExactMatchesRecursive(cfg.root.transform, scene6TorchObjectName, torchRoots);

        for (int i = 0; i < torchRoots.Count; i++)
            EnsureScene6StaticTorchFlameForTarget(torchRoots[i] != null ? torchRoots[i].transform : null);

        if (verboseLogs && torchRoots.Count == 0)
            Debug.LogWarning($"[SceneGroupManager] Nessun target '{scene6TorchObjectName}' trovato in {sceneName} per la fiammella statica.");
    }

    private void EnsureScene6StaticTorchFlameForTarget(Transform target)
    {
        if (target == null)
            return;

        Transform existing = target.Find(scene6TorchEffectRootName);
        if (existing != null)
        {
            existing.SetPositionAndRotation(ResolveScene6TorchEffectWorldPosition(target), Quaternion.identity);
            existing.localScale = GetInverseAbsLossyScale(target);
            existing.gameObject.SetActive(true);
            return;
        }

        GameObject effectRoot = new GameObject(scene6TorchEffectRootName);
        Transform effectTransform = effectRoot.transform;
        effectTransform.SetPositionAndRotation(ResolveScene6TorchEffectWorldPosition(target), Quaternion.identity);
        effectTransform.SetParent(target, true);
        effectTransform.localScale = GetInverseAbsLossyScale(target);

        Material flameMaterial = ResolveScene6TorchFlameMaterial();
        CreateScene6TorchQuad(effectTransform, "GlowOuter_A", Quaternion.identity, scene6TorchGlowWidth, scene6TorchGlowHeight, scene6TorchGlowColor, flameMaterial);
        CreateScene6TorchQuad(effectTransform, "GlowOuter_B", Quaternion.Euler(0f, 90f, 0f), scene6TorchGlowWidth, scene6TorchGlowHeight, scene6TorchGlowColor, flameMaterial);
        CreateScene6TorchQuad(effectTransform, "GlowInner_A", Quaternion.identity, scene6TorchGlowWidth * scene6TorchInnerGlowScale, scene6TorchGlowHeight * scene6TorchInnerGlowScale, scene6TorchInnerGlowColor, flameMaterial);
        CreateScene6TorchQuad(effectTransform, "GlowInner_B", Quaternion.Euler(0f, 90f, 0f), scene6TorchGlowWidth * scene6TorchInnerGlowScale, scene6TorchGlowHeight * scene6TorchInnerGlowScale, scene6TorchInnerGlowColor, flameMaterial);

        Light torchLight = new GameObject("TorchLight", typeof(Light)).GetComponent<Light>();
        torchLight.transform.SetParent(effectTransform, false);
        torchLight.transform.localPosition = new Vector3(0f, scene6TorchGlowHeight * 0.22f, 0f);
        torchLight.type = LightType.Point;
        torchLight.color = scene6TorchLightColor;
        torchLight.intensity = scene6TorchLightIntensity;
        torchLight.range = scene6TorchLightRange;
        torchLight.shadows = LightShadows.None;
        torchLight.renderMode = LightRenderMode.Auto;
        torchLight.enabled = true;
    }

    private Vector3 ResolveScene6TorchEffectWorldPosition(Transform target)
    {
        if (target == null)
            return Vector3.zero;

        float verticalOffset = scene6TorchHeightAboveBounds + scene6TorchVerticalOffsetAdjustment;

        Renderer[] renderers = target.GetComponentsInChildren<Renderer>(true);
        Renderer firstRenderer = null;
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] == null)
                continue;

            firstRenderer = renderers[i];
            break;
        }

        if (firstRenderer == null)
            return target.position + (Vector3.up * verticalOffset);

        Bounds combinedBounds = firstRenderer.bounds;
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null)
                continue;

            combinedBounds.Encapsulate(renderer.bounds);
        }

        Vector3 worldPosition = combinedBounds.center;
        worldPosition.y = combinedBounds.max.y + verticalOffset;
        return worldPosition;
    }

    private Material ResolveScene6TorchFlameMaterial()
    {
        Texture2D glowTexture = Resources.Load<Texture2D>(scene6TorchGlowTextureResourceName);
        if (glowTexture == null && !_hasWarnedMissingScene6TorchTexture)
        {
            _hasWarnedMissingScene6TorchTexture = true;
            Debug.LogWarning($"[SceneGroupManager] Texture Resources '{scene6TorchGlowTextureResourceName}' non trovata. La fiammella usera' solo la luce.");
        }

        if (_runtimeScene6TorchFlameMaterial == null)
        {
            Shader shader = Shader.Find("Particles/Additive")
                ?? Shader.Find("Legacy Shaders/Particles/Additive")
                ?? Shader.Find("Universal Render Pipeline/Particles/Unlit")
                ?? Shader.Find("Sprites/Default")
                ?? Shader.Find("Unlit/Transparent");

            if (shader == null)
            {
                if (!_hasWarnedMissingScene6TorchShader)
                {
                    _hasWarnedMissingScene6TorchShader = true;
                    Debug.LogWarning("[SceneGroupManager] Nessuno shader adatto trovato per la fiammella statica.");
                }

                return null;
            }

            _runtimeScene6TorchFlameMaterial = new Material(shader)
            {
                name = "Runtime_Scene6StaticTorchGlow",
                hideFlags = HideFlags.DontSave
            };
        }

        if (_runtimeScene6TorchFlameMaterial != null)
        {
            ApplyScene6TorchMaterialColor(_runtimeScene6TorchFlameMaterial, scene6TorchGlowColor);

            if (glowTexture != null)
            {
                if (_runtimeScene6TorchFlameMaterial.HasProperty("_MainTex"))
                    _runtimeScene6TorchFlameMaterial.SetTexture("_MainTex", glowTexture);
                if (_runtimeScene6TorchFlameMaterial.HasProperty("_BaseMap"))
                    _runtimeScene6TorchFlameMaterial.SetTexture("_BaseMap", glowTexture);
            }

            if (_runtimeScene6TorchFlameMaterial.HasProperty("_Cull"))
                _runtimeScene6TorchFlameMaterial.SetFloat("_Cull", (float)CullMode.Off);
        }

        return _runtimeScene6TorchFlameMaterial;
    }

    private static void ApplyScene6TorchMaterialColor(Material material, Color color)
    {
        if (material == null)
            return;

        if (material.HasProperty("_Color"))
            material.SetColor("_Color", color);
        if (material.HasProperty("_BaseColor"))
            material.SetColor("_BaseColor", color);
        if (material.HasProperty("_TintColor"))
            material.SetColor("_TintColor", color);
        if (material.HasProperty("_EmissionColor"))
            material.SetColor("_EmissionColor", color * 0.75f);
    }

    private static Vector3 GetInverseAbsLossyScale(Transform target)
    {
        if (target == null)
            return Vector3.one;

        Vector3 lossyScale = target.lossyScale;
        return new Vector3(
            1f / Mathf.Max(0.0001f, Mathf.Abs(lossyScale.x)),
            1f / Mathf.Max(0.0001f, Mathf.Abs(lossyScale.y)),
            1f / Mathf.Max(0.0001f, Mathf.Abs(lossyScale.z)));
    }

    private static void CreateScene6TorchQuad(Transform parent, string name, Quaternion localRotation, float width, float height, Color tint, Material sharedMaterial)
    {
        if (parent == null)
            return;

        GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.name = name;
        quad.transform.SetParent(parent, false);
        quad.transform.localPosition = new Vector3(0f, height * 0.34f, 0f);
        quad.transform.localRotation = localRotation;
        quad.transform.localScale = new Vector3(width, height, 1f);

        Collider collider = quad.GetComponent<Collider>();
        if (collider != null)
            UnityEngine.Object.Destroy(collider);

        MeshRenderer renderer = quad.GetComponent<MeshRenderer>();
        if (renderer != null)
        {
            renderer.sharedMaterial = sharedMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;

            if (renderer.sharedMaterial != null)
            {
                MaterialPropertyBlock block = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(block);
                if (renderer.sharedMaterial.HasProperty("_Color"))
                    block.SetColor("_Color", tint);
                if (renderer.sharedMaterial.HasProperty("_BaseColor"))
                    block.SetColor("_BaseColor", tint);
                if (renderer.sharedMaterial.HasProperty("_TintColor"))
                    block.SetColor("_TintColor", tint);
                renderer.SetPropertyBlock(block);
            }
        }
    }

    private void ReleaseScene6TorchFlameMaterial()
    {
        if (_runtimeScene6TorchFlameMaterial == null)
            return;

        Destroy(_runtimeScene6TorchFlameMaterial);
        _runtimeScene6TorchFlameMaterial = null;
    }

    private void TryStartDonnaScene6Sequence(VirtualScene cfg)
    {
        if (!autoPlayDonnaScene6Animation || cfg == null || cfg.root == null)
            return;

        string sceneName = string.IsNullOrWhiteSpace(cfg.name) ? cfg.root.name : cfg.name;
        if (!string.Equals(sceneName, donnaSceneName, StringComparison.OrdinalIgnoreCase))
            return;

        List<Animator> animators = FindAnimatorsForDonne(cfg.root.transform);
        animators = KeepBestAnimatorPerDonna(animators);
        if (animators.Count == 0)
        {
            if (verboseLogs)
                Debug.LogWarning($"[SceneGroupManager] Nessun animator donna trovato in '{cfg.root.name}'.");
            return;
        }

        StopDonnaSequence();

        for (int i = 0; i < animators.Count; i++)
        {
            Animator animator = animators[i];
            if (animator == null)
                continue;

            string ownerToken = ResolveDonnaClipOwnerToken(animator);
            Coroutine routine = StartCoroutine(PlayDonnaMovementThenIdle(animator, cfg.root, ownerToken));
            _donnaRoutines.Add(routine);
        }
    }

    private List<Animator> KeepBestAnimatorPerDonna(List<Animator> animators)
    {
        List<Animator> fallback = animators ?? new List<Animator>(0);
        if (fallback.Count <= 2)
            return fallback;

        Animator bestUno = null;
        Animator bestDue = null;
        int bestUnoScore = int.MinValue;
        int bestDueScore = int.MinValue;

        for (int i = 0; i < fallback.Count; i++)
        {
            Animator animator = fallback[i];
            if (animator == null)
                continue;

            string ownerToken = ResolveDonnaClipOwnerToken(animator);
            bool isDue = IsDonnaDue(ownerToken, animator);
            int score = ScoreDonnaAnimatorCandidate(animator, isDue);

            if (isDue)
            {
                if (score > bestDueScore)
                {
                    bestDue = animator;
                    bestDueScore = score;
                }
            }
            else
            {
                if (score > bestUnoScore)
                {
                    bestUno = animator;
                    bestUnoScore = score;
                }
            }
        }

        List<Animator> result = new List<Animator>(2);
        if (bestUno != null)
            result.Add(bestUno);
        if (bestDue != null && bestDue != bestUno)
            result.Add(bestDue);

        if (result.Count == 0)
            return fallback;

        return result;
    }

    private int ScoreDonnaAnimatorCandidate(Animator animator, bool isDue)
    {
        if (animator == null)
            return int.MinValue;

        Transform t = animator.transform;
        int score = 0;

        if (t.GetComponent<SkinnedMeshRenderer>() != null)
            score += 20;
        if (t.GetComponentInChildren<SkinnedMeshRenderer>(true) != null)
            score += 10;

        string preferredName = isDue ? donnaSecondAnimatorObjectName : donnaAnimatorObjectName;
        if (!string.IsNullOrWhiteSpace(preferredName) &&
            t.name.IndexOf(preferredName, StringComparison.OrdinalIgnoreCase) >= 0)
        {
            score += 30;
        }

        if (t.name.IndexOf("_arm", StringComparison.OrdinalIgnoreCase) >= 0)
            score += 8;

        if (t.parent != null && t.parent.name.IndexOf("donna", StringComparison.OrdinalIgnoreCase) >= 0)
            score += 4;

        return score;
    }

    private List<Animator> FindAnimatorsForDonne(Transform root)
    {
        List<Animator> result = new List<Animator>(4);
        if (root == null)
            return result;

        HashSet<int> seen = new HashSet<int>();
        List<Transform> candidates = CollectDonnaCandidateTransforms(root);
        for (int i = 0; i < candidates.Count; i++)
        {
            Transform candidate = candidates[i];
            if (candidate == null)
                continue;

            string ownerToken = ResolveDonnaClipOwnerTokenFromName(candidate.name);
            Animator ensured = EnsureAnimatorOnTransform(candidate, ownerToken);
            TryAddAnimator(ensured, result, seen);
        }

        Animator[] allAnimators = root.GetComponentsInChildren<Animator>(true);
        for (int i = 0; i < allAnimators.Length; i++)
            TryAddAnimator(allAnimators[i], result, seen);

        if (result.Count == 0 && allAnimators.Length > 0)
            TryAddAnimator(allAnimators[0], result, seen);

        return result;
    }

    private List<Transform> CollectDonnaCandidateTransforms(Transform root)
    {
        List<Transform> result = new List<Transform>(4);
        HashSet<int> seen = new HashSet<int>();

        AddAnimatorMatches(root, donnaAnimatorObjectName, result, seen);
        AddAnimatorMatches(root, donnaSecondAnimatorObjectName, result, seen);

        if (result.Count < 2)
        {
            Transform[] all = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                Transform t = all[i];
                if (t == null)
                    continue;

                if (t.name.IndexOf("donna", StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                if (t.GetComponentInChildren<SkinnedMeshRenderer>(true) == null &&
                    t.GetComponent<SkinnedMeshRenderer>() == null)
                {
                    continue;
                }

                TryAddTransformCandidate(t, result, seen);
            }
        }

        if (result.Count == 0)
        {
            Animator[] allAnimators = root.GetComponentsInChildren<Animator>(true);
            for (int i = 0; i < allAnimators.Length; i++)
                TryAddTransformCandidate(allAnimators[i] != null ? allAnimators[i].transform : null, result, seen);
        }

        return result;
    }

    private void AddAnimatorMatches(Transform root, string nameToken, List<Transform> destination, HashSet<int> seen)
    {
        if (string.IsNullOrWhiteSpace(nameToken))
            return;

        Transform named = FindChildRecursive(root, nameToken);
        TryAddTransformCandidate(named, destination, seen);
    }

    private static void TryAddTransformCandidate(Transform transform, List<Transform> destination, HashSet<int> seen)
    {
        if (transform == null)
            return;

        int id = transform.GetInstanceID();
        if (!seen.Add(id))
            return;

        destination.Add(transform);
    }

    private static void TryAddAnimator(Animator animator, List<Animator> destination, HashSet<int> seen)
    {
        if (animator == null)
            return;

        int id = animator.GetInstanceID();
        if (!seen.Add(id))
            return;

        destination.Add(animator);
    }

    private Animator EnsureAnimatorOnTransform(Transform target, string ownerToken)
    {
        if (target == null)
            return null;

        Animator animator = target.GetComponent<Animator>();
        if (animator == null)
            animator = target.GetComponentInChildren<Animator>(true);

        if (animator == null)
        {
            animator = target.gameObject.AddComponent<Animator>();
            if (verboseLogs)
                Debug.Log($"[SceneGroupManager] Aggiunto Animator runtime su '{target.name}'.");
        }

        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

        if (animator.avatar == null)
            TryAssignBestAvatar(animator, ownerToken);

        return animator;
    }

    private void TryAssignBestAvatar(Animator animator, string ownerToken)
    {
        if (animator == null)
            return;
        if (animator.avatar != null)
            return;

        Avatar[] avatars = Resources.FindObjectsOfTypeAll<Avatar>();
        Avatar best = null;
        int bestScore = int.MinValue;
        bool isDue = !string.IsNullOrWhiteSpace(ownerToken) &&
                     ownerToken.IndexOf("due", StringComparison.OrdinalIgnoreCase) >= 0;

        for (int i = 0; i < avatars.Length; i++)
        {
            Avatar avatar = avatars[i];
            if (avatar == null || !avatar.isValid)
                continue;

            string avatarName = avatar.name ?? string.Empty;
            int score = 0;
            if (avatarName.IndexOf("donna", StringComparison.OrdinalIgnoreCase) >= 0) score += 6;
            if (!string.IsNullOrWhiteSpace(ownerToken) &&
                avatarName.IndexOf(ownerToken, StringComparison.OrdinalIgnoreCase) >= 0) score += 12;
            if (isDue && avatarName.IndexOf("due", StringComparison.OrdinalIgnoreCase) >= 0) score += 4;
            if (!isDue && avatarName.IndexOf("uno", StringComparison.OrdinalIgnoreCase) >= 0) score += 4;
            if (avatarName.IndexOf("arm", StringComparison.OrdinalIgnoreCase) >= 0) score += 2;

            if (score > bestScore)
            {
                best = avatar;
                bestScore = score;
            }
        }

        if (best != null && bestScore > 0)
        {
            animator.avatar = best;
            if (verboseLogs)
                Debug.Log($"[SceneGroupManager] Avatar '{best.name}' assegnato a '{animator.name}'.");
        }
    }

    private static Transform FindChildRecursive(Transform root, string containsName)
    {
        if (root == null || string.IsNullOrWhiteSpace(containsName))
            return null;

        if (root.name.IndexOf(containsName, StringComparison.OrdinalIgnoreCase) >= 0)
            return root;

        for (int i = 0; i < root.childCount; i++)
        {
            Transform found = FindChildRecursive(root.GetChild(i), containsName);
            if (found != null)
                return found;
        }

        return null;
    }

    private string ResolveDonnaClipOwnerToken(Animator animator)
    {
        if (animator == null)
            return string.Empty;

        return ResolveDonnaClipOwnerTokenFromName(animator.name);
    }

    private string ResolveDonnaClipOwnerTokenFromName(string objectName)
    {
        string name = objectName ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(donnaAnimatorObjectName) &&
            name.IndexOf(donnaAnimatorObjectName, StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return donnaUnoClipToken;
        }

        if (!string.IsNullOrWhiteSpace(donnaSecondAnimatorObjectName) &&
            name.IndexOf(donnaSecondAnimatorObjectName, StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return donnaDueClipToken;
        }

        if (name.IndexOf("due", StringComparison.OrdinalIgnoreCase) >= 0)
            return donnaDueClipToken;

        return donnaUnoClipToken;
    }

    private bool IsDonnaDue(string ownerToken, Animator animator)
    {
        if (!string.IsNullOrWhiteSpace(ownerToken) &&
            ownerToken.IndexOf("due", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return true;
        }

        return animator != null &&
               animator.name.IndexOf("due", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private IEnumerator PlayDonnaMovementThenIdle(Animator animator, GameObject sceneRoot, string ownerToken)
    {
        bool isDonnaDue = IsDonnaDue(ownerToken, animator);
        AnimationClip movement = FindMovementClip(animator, ownerToken, isDonnaDue);
        AnimationClip idle = FindIdleClip(animator, ownerToken, isDonnaDue);

        if (movement == null && idle == null)
        {
            if (verboseLogs)
                Debug.LogWarning($"[SceneGroupManager] Donna '{animator?.name}': clip movimento/idle non trovate.");
            yield break;
        }

        if (movement != null)
        {
            PlayClipWithPlayable(animator, movement);
            if (verboseLogs)
                Debug.Log($"[SceneGroupManager] Donna '{animator.name}' -> movimento '{movement.name}'");

            yield return new WaitForSeconds(Mathf.Max(0.05f, movement.length));
        }

        while (sceneRoot != null && sceneRoot.activeInHierarchy && animator != null && animator.isActiveAndEnabled)
        {
            if (idle == null)
                yield break;

            PlayClipWithPlayable(animator, idle);
            if (verboseLogs)
                Debug.Log($"[SceneGroupManager] Donna '{animator.name}' -> idle '{idle.name}'");

            yield return new WaitForSeconds(Mathf.Max(0.05f, idle.length));
        }
    }

    private AnimationClip FindMovementClip(Animator animator, string ownerToken, bool isDonnaDue)
    {
        AnimationClip explicitClip = isDonnaDue ? donnaDueMovementClip : donnaUnoMovementClip;
        if (explicitClip != null)
            return explicitClip;

        return FindClip(animator, donnaMovementToken, ownerToken);
    }

    private AnimationClip FindIdleClip(Animator animator, string ownerToken, bool isDonnaDue)
    {
        if (isDonnaDue)
        {
            if (donnaDueIdlePostClip != null)
                return donnaDueIdlePostClip;
            if (donnaDueIdlePreClip != null)
                return donnaDueIdlePreClip;
        }
        else if (donnaUnoIdleClip != null)
        {
            return donnaUnoIdleClip;
        }

        if (isDonnaDue && !string.IsNullOrWhiteSpace(donnaDuePreferredIdleToken))
        {
            AnimationClip preferredDueIdle = FindClip(animator, donnaDuePreferredIdleToken, ownerToken);
            if (preferredDueIdle != null)
                return preferredDueIdle;
        }

        AnimationClip defaultIdle = FindClip(animator, donnaIdleToken, ownerToken);
        if (defaultIdle != null)
            return defaultIdle;

        if (isDonnaDue)
            return FindClip(animator, "IdlePre", ownerToken);

        return null;
    }

    private AnimationClip FindClip(Animator animator, string token, string ownerToken)
    {
        if (animator == null || string.IsNullOrWhiteSpace(token))
            return null;

        RuntimeAnimatorController controller = animator.runtimeAnimatorController;
        AnimationClip match = FindClipInArray(controller != null ? controller.animationClips : null, token, ownerToken, false);
        if (match != null)
            return match;

        AnimationClip[] loaded = Resources.FindObjectsOfTypeAll<AnimationClip>();
        match = FindClipInArray(loaded, token, ownerToken, true);
        if (match != null)
            return match;

        if (!string.IsNullOrWhiteSpace(ownerToken))
        {
            match = FindClipInArray(controller != null ? controller.animationClips : null, token, null, false);
            if (match != null)
                return match;

            match = FindClipInArray(loaded, token, null, true);
            if (match != null)
                return match;
        }

        return null;
    }

    private static AnimationClip FindClipInArray(AnimationClip[] clips, string token, string ownerToken, bool requireDonnaKeyword)
    {
        if (clips == null || clips.Length == 0 || string.IsNullOrWhiteSpace(token))
            return null;

        for (int i = 0; i < clips.Length; i++)
        {
            AnimationClip c = clips[i];
            if (c == null)
                continue;

            string clipName = c.name ?? string.Empty;
            if (clipName.StartsWith("__preview__", StringComparison.OrdinalIgnoreCase))
                continue;
            if (requireDonnaKeyword && clipName.IndexOf("donna", StringComparison.OrdinalIgnoreCase) < 0)
                continue;
            if (clipName.IndexOf(token, StringComparison.OrdinalIgnoreCase) < 0)
                continue;
            if (!string.IsNullOrWhiteSpace(ownerToken) &&
                clipName.IndexOf(ownerToken, StringComparison.OrdinalIgnoreCase) < 0)
            {
                continue;
            }

            return c;
        }

        return null;
    }

    private void PlayClipWithPlayable(Animator animator, AnimationClip clip)
    {
        if (animator == null || clip == null)
            return;

        StopDonnaGraph(animator);

        int animatorId = animator.GetInstanceID();
        PlayableGraph graph = PlayableGraph.Create($"DonnaSequence_{animatorId}");
        graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);

        AnimationPlayableOutput output = AnimationPlayableOutput.Create(graph, "Animation", animator);
        AnimationClipPlayable playable = AnimationClipPlayable.Create(graph, clip);
        output.SetSourcePlayable(playable);

        graph.Play();
        _donnaGraphs[animatorId] = graph;
    }

    private void StopDonnaSequence()
    {
        for (int i = 0; i < _donnaRoutines.Count; i++)
        {
            Coroutine routine = _donnaRoutines[i];
            if (routine != null)
                StopCoroutine(routine);
        }

        _donnaRoutines.Clear();
        StopAllDonnaGraphs();
    }

    private void StopDonnaGraph(Animator animator)
    {
        if (animator == null)
            return;

        int id = animator.GetInstanceID();
        if (!_donnaGraphs.TryGetValue(id, out PlayableGraph graph))
            return;

        if (graph.IsValid())
            graph.Destroy();

        _donnaGraphs.Remove(id);
    }

    private void StopAllDonnaGraphs()
    {
        foreach (var kvp in _donnaGraphs)
        {
            PlayableGraph graph = kvp.Value;
            if (graph.IsValid())
                graph.Destroy();
        }

        _donnaGraphs.Clear();
    }

    private VirtualScene GetCurrentCfg()
    {
        if (scenes == null) return null;
        if (_index < 0 || _index >= scenes.Count) return null;
        return scenes[_index];
    }

    private void SyncToCurrentlyActive()
    {
        if (scenes == null || scenes.Count == 0)
            return;

        for (int i = 0; i < scenes.Count; i++)
        {
            var s = scenes[i];
            if (s != null && s.root != null && s.root.activeInHierarchy)
            {
                _index = i - 1; // così Next() va alla successiva
                if (verboseLogs) Debug.Log($"[SceneGroupManager] Sync: scena attiva trovata -> {s.root.name} (index {i})");
                return;
            }
        }

        if (verboseLogs) Debug.Log("[SceneGroupManager] Sync: nessuna scena attiva trovata.");
    }

    private VirtualScene FindConfigForObject(GameObject obj)
    {
        if (obj == null || scenes == null) return null;

        Transform t = obj.transform;

        // 1) match diretto root
        for (int i = 0; i < scenes.Count; i++)
        {
            var s = scenes[i];
            if (s != null && s.root == obj)
                return s;
        }

        // 2) match se obj è figlio del root
        for (int i = 0; i < scenes.Count; i++)
        {
            var s = scenes[i];
            if (s != null && s.root != null)
            {
                if (t.IsChildOf(s.root.transform))
                    return s;
            }
        }

        return null;
    }

    private int FindActiveSceneIndex()
    {
        if (scenes == null || scenes.Count == 0)
            return -1;

        for (int i = 0; i < scenes.Count; i++)
        {
            VirtualScene s = scenes[i];
            if (s != null && s.root != null && s.root.activeInHierarchy)
                return i;
        }

        return -1;
    }
}
