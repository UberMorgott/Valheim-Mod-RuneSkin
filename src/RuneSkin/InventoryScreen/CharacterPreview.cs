using System;
using System.Collections.Generic;
using RuneSkin.Theme;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace RuneSkin.InventoryScreen
{
    // Live 3D preview of the local player in the inventory (replaces the carved silhouette). Self-contained: a RawImage
    // placed by its host (Place(stage, figure) in canvas units: the image covers the stage, the body is framed to the
    // figure box's height inside it) that owns everything behind it while the inventory is open:
    // - clone: the vanilla character-select approach (FejdStartup.SetupCharacterPreview: player prefab instantiated
    //   with ZNetView.m_forceDisableInit, so its ZNetView destroys itself in Awake and never gets a ZDO), but stripped
    //   BEFORE activation to visuals only (no Player / Character / AI / Rigidbody / colliders / audio / sfx / lights;
    //   Awake of the stripped scripts never runs). Its VisEquipment stays disabled (not in VisEquipment.Instances, no
    //   per-frame update) and is filled by the player's own Player.SetupVisEquipment(clone, false) - the call vanilla
    //   uses for ragdolls (Humanoid.OnRagdollCreated) - so armour, weapons (hand and back), cape, utility, trinket,
    //   hair / beard, model and skin / hair colours and other mods' items follow the standard VisEquipment path.
    //   Refreshed only when the player's equipment / appearance changes (MarkDirty from InventoryAdapter hooks).
    //   No ZDO is ever written (the clone has none): multiplayer-neutral.
    // - own culling layer (first unnamed layer), own camera (disabled, rendered manually) and light rig (key / fill /
    //   rim lights culling only that layer); while it renders, the world's directional lights, fog and ambient are
    //   swapped for the rig's and restored right after: the preview looks the same at any time of day / weather.
    // - drag on the image turns the clone; the idle animation runs on the clone's Animator (no events, no root motion).
    // - lifecycle: built once per local player - prewarmed while the inventory is closed (Prewarm, called by
    //   InventorySkin every frame: the first frame with a live local player, i.e. behind the spawn's loading screen),
    //   else on the first open - and kept: a close only PARKS it (rig deactivated: no camera, lights, animator or cloth
    //   work; the render texture's GPU memory released, the object kept), the next open re-activates the rig. A
    //   rebuild per open (instantiate the player prefab, strip it, 4 animator warm-up steps, SetupVisEquipment of every
    //   worn item, 2 x Resources.FindObjectsOfTypeAll<Material / Mesh> material-ownership windows, a new MSAA render
    //   texture) was the Tab hitch (user 2026-10-27: "window not optimized"). Torn down (camera, clone, lights, render
    //   texture, runtime materials and meshes) only when the player changes / logs out or this component is destroyed.
    //   A disable while the inventory stays open (an ancestor toggled for a frame) keeps the rig live. LiveClones /
    //   LiveTextures count what exists and History lists the last builds / parks / teardowns with their reason
    //   (autotest leak check; KeepsClone tells the harness this contract).
    public sealed class CharacterPreview : MonoBehaviour, IBeginDragHandler, IDragHandler
    {
        public static CharacterPreview Instance { get; private set; }

        // Leak counters: objects this component created and has not destroyed yet.
        public static int LiveClones { get; private set; }
        public static int LiveTextures { get; private set; }

        // Equipment / appearance revision of the local player (MarkDirty) and refreshes done (autotest).
        private static int _revision;
        public int Refreshes { get; private set; }
        public int Builds { get; private set; }

        // Last lifecycle events ("f<frame> build" / "f<frame> teardown <reason>"), newest last (autotest diagnostics).
        private static readonly Queue<string> Events = new Queue<string>();
        public static string History => string.Join(" | ", Events);

        private static void Note(string what)
        {
            if (Events.Count >= 24) Events.Dequeue();
            Events.Enqueue($"f{Time.frameCount} {what}");
        }

        // The clone survives a close (parked, header): Live = rig active with a created texture (shown / showable now).
        public bool Live => _clone && _rig && _rig.activeSelf && _rt && _rt.IsCreated();
        // Harness contract: a close parks the clone instead of tearing it down (builds stay flat over open / close cycles).
        public static bool KeepsClone => true;
        public RenderTexture Texture => _rt;
        public GameObject Clone => _clone;
        public int Layer => _layer;
        public float Yaw => _yaw;
        // Share of the stage height the body is framed to (figure box / stage, Place) and the framed body height in
        // metres (autotest preview-framed: projects feet and BodyH through the camera).
        public float FigureShare => _share;
        public static float FramedBodyHeight => BodyH;

        // World point far from any playable area (worlds span about +-10500 m on x/z, terrain below ~1000 m).
        private static readonly Vector3 Origin = new Vector3(0f, 12000f, 0f);
        // BodyH: standing height framed to the figure box; SpanW: width that must stay inside the stage (sword in hand,
        // shield, cape, back weapons: the old 1.2 m body width cut them at the image edge, 2026-10-27 user report).
        private const float BodyH = 2.0f, SpanW = 2.2f, CenterY = 0.98f, Pad = 0.06f, Fov = 30f, DegPerPixel = 0.45f;

        private RawImage _image;
        private RectTransform _rect;
        private GameObject _rig, _clone;
        private VisEquipment _vis;
        private Camera _camera;
        private RenderTexture _rt;
        private Player _player;
        private int _layer = -1, _applied = -1;
        private float _yaw, _share = 1f;
        private bool _placed, _hidden, _failed, _alphaChecked;
        // Runtime materials created while this component built / refreshed the clone (destroyed on teardown).
        private readonly List<Material> _owned = new List<Material>();
        private readonly List<Mesh> _ownedMeshes = new List<Mesh>();
        private static bool _ownedLogged;
        private static readonly int VisibleHash = Animator.StringToHash("visible");
        private static bool _opaqueClear;
        private readonly List<Light> _worldLights = new List<Light>();
        private readonly List<Light> _swapped = new List<Light>();
        // The rig's key / fill / rim lights: enabled only inside Render. A light's culling mask does not keep it off the
        // world under the main camera's deferred path (Unity supports only 4 excluded layers there; the rig excludes 31),
        // so lights left enabled lit the whole world while the inventory was open (user 2026-10-23: "as if day comes";
        // run 20261023-161531 hud2 vs hud2-inv: world strip luma 209 -> 224, blue +35, i.e. a warm-white light added).
        private readonly List<Light> _rigLights = new List<Light>();

        public static CharacterPreview Create(RectTransform parent, string name)
        {
            var rt = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
            rt.SetParent(parent, false);
            var image = rt.gameObject.AddComponent<RawImage>();
            image.raycastTarget = true; // drag to rotate
            image.color = Color.white;
            image.enabled = false;
            DollCell.Mark(image, "content", "live 3D character preview (render texture)");
            var preview = rt.gameObject.AddComponent<CharacterPreview>();
            preview._image = image;
            preview._rect = rt;
            rt.gameObject.SetActive(false);
            return preview;
        }

        // The local player's equipment or appearance changed (InventoryAdapter hooks): refresh on the next frame.
        public static void MarkDirty() => _revision++;

        public bool Failed => _failed;

        // Host placement: the stage the image covers and the figure box the body is framed to (canvas units, top-left
        // origin; same canvas, so the body follows the GUI / skin scale with the layout); an empty stage only hides the
        // image while the inventory stays open (the clone is kept: no rebuild on a transient layout state).
        public void Place(RectTransform canvas, Box box, Box figure)
        {
            if (_failed || !IsOpen())
            {
                if (gameObject.activeSelf) Close("place while closed");
                return;
            }
            _placed = true;
            _hidden = box.IsEmpty || figure.IsEmpty;
            _share = PreviewMath.Share(figure.H, box.H);
            if (!gameObject.activeSelf) gameObject.SetActive(true);
            if (!_hidden) Geo.Place(canvas, _rect, box);
        }

        // The inventory's own open state. InventoryGui.IsVisible() stays true one frame after Hide (m_hiddenFrames <= 1,
        // InventoryGui.cs:1019): using it rebuilt the clone once per close (in-game run 20261022-174939, builds 18 / 10).
        private static bool IsOpen()
        {
            var gui = InventoryGui.instance;
            return gui && gui.m_animator && gui.m_animator.GetBool(VisibleHash);
        }

        // Hides the image and parks the clone (inventory closed).
        public void Close() => Close("inventory hidden");

        private void Close(string why)
        {
            _placed = false;
            Park(why);
            if (gameObject.activeSelf) gameObject.SetActive(false);
        }

        private void Awake() => Instance = this;

        // Closed (or no longer placed): park now. Still open: a transient disable, the rig stays live (header).
        private void OnDisable()
        {
            if (!_placed || !IsOpen()) Park("disabled");
        }

        // Inventory closed: build (and dress) the clone ahead of the first open, then park it. Called every frame by
        // InventorySkin (this object is inactive while closed); a no-op once built for the current player. Skipped while
        // this component is disabled (autotest control batch) or the preview failed.
        public void Prewarm()
        {
            if (!enabled || _failed || IsOpen()) return;
            var player = Player.m_localPlayer;
            if (!player || player.IsDead()) return;
            if (_clone && _player == player) return;
            if (_clone) Teardown("player changed");
            if (!Build(player)) return;
            Refresh();
            Note("prewarm");
            Park("prewarmed");
        }

        // Deactivates the rig (camera, lights, animators, cloth stop) and releases the render texture's GPU memory; the
        // clone, its materials and the texture object are kept for the next open.
        private void Park(string why)
        {
            if (_rig && _rig.activeSelf)
            {
                _rig.SetActive(false);
                Note("park " + why);
            }
            if (_rt && _rt.IsCreated()) _rt.Release();
            if (_image) _image.enabled = false;
        }

        // Next open: the parked rig back on, the world's directional lights re-read (the set Render swaps out).
        private void Unpark()
        {
            if (!_rig || _rig.activeSelf) return;
            _rig.SetActive(true);
            _worldLights.Clear();
            foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (l && l.type == LightType.Directional && !l.transform.IsChildOf(_rig.transform)) _worldLights.Add(l);
            Note("unpark");
        }

        private void OnDestroy()
        {
            Teardown("destroyed");
            if (Instance == this) Instance = null;
        }

        private void LateUpdate()
        {
            var player = Player.m_localPlayer;
            if (!_placed || !IsOpen() || !player || player.IsDead())
            {
                Close(!_placed ? "not placed" : !IsOpen() ? "inventory closed" : "no live player");
                return;
            }
            if (_clone && _player != player) Teardown("player changed");
            if (!_clone && !Build(player)) return;
            Unpark();
            if (_hidden)
            {
                _image.enabled = false;
                return;
            }
            // Under a full window (stats, compendium) the doll panel is gated (FullWindow, RenderGate): the camera would
            // render the rig every frame into a texture nobody sees. The texture keeps its last frame for the return.
            if (RuneSkin.Restyle.RenderGate.IsConcealed(transform)) return;
            var px = ScreenSize();
            var size = PreviewMath.TextureSize(px.x, px.y, 1f);
            if (size.W <= 0) return;
            if (!_rt || PreviewMath.NeedsResize(_rt.width, _rt.height, size.W, size.H)) MakeTexture(size.W, size.H);
            else if (!_rt.IsCreated()) _rt.Create(); // released by Park
            if (_applied != _revision) Refresh();
            _image.enabled = true;
            Frame();
            Render();
            if (!_alphaChecked)
            {
                _alphaChecked = true;
                CheckAlpha();
            }
        }

        // On-screen pixel size of the image (any canvas mode / scale): the texture matches the pixels it covers.
        private Vector2 ScreenSize()
        {
            var canvas = _image.canvas ? _image.canvas.rootCanvas : null;
            var cam = canvas && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            _rect.GetWorldCorners(Corners);
            var a = RectTransformUtility.WorldToScreenPoint(cam, Corners[0]);
            var b = RectTransformUtility.WorldToScreenPoint(cam, Corners[2]);
            return new Vector2(Mathf.Abs(b.x - a.x), Mathf.Abs(b.y - a.y));
        }

        private static readonly Vector3[] Corners = new Vector3[4];

        // ------------------------------------------------------------------ build / teardown

        private bool Build(Player player)
        {
            var prefab = Game.instance ? Game.instance.m_playerPrefab : null;
            if (!prefab) return false;
            if (_layer < 0) _layer = PreviewMath.PickLayer(LayerMask.LayerToName);
            if (_layer < 0)
            {
                Fail("no free culling layer");
                return false;
            }
            var before = MaterialIds();
            try
            {
                _player = player;
                _rig = new GameObject("RuneSkinPreviewRig");
                _rig.SetActive(false);
                _rig.transform.position = Origin;
                _clone = Instantiate(prefab, _rig.transform);
                LiveClones++;
                _clone.name = "RuneSkinPreviewCharacter";
                _clone.transform.localPosition = Vector3.zero;
                _clone.transform.localRotation = Quaternion.Euler(0f, _yaw, 0f);
                _vis = _clone.GetComponent<VisEquipment>();
                if (!_vis) throw new InvalidOperationException("player prefab has no VisEquipment");
                _vis.m_playerComponent = null;
                _vis.enabled = false; // Awake still runs on activation; never joins VisEquipment.Instances
                Strip(_clone, build: true);
                BuildRig();
                SetLayer(_rig.transform, _layer);
                var force = ZNetView.m_forceDisableInit;
                ZNetView.m_forceDisableInit = true; // the clone's ZNetView destroys itself in Awake (no ZDO)
                try { _rig.SetActive(true); }
                finally { ZNetView.m_forceDisableInit = force; }
                foreach (var animator in _clone.GetComponentsInChildren<Animator>(true))
                {
                    animator.updateMode = AnimatorUpdateMode.Normal;
                    animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                    animator.applyRootMotion = false;
                    animator.fireEvents = false; // the receivers (CharacterAnimEvent) are stripped
                    animator.keepAnimatorStateOnDisable = true; // a park / unpark resumes the idle loop (no spawn pose)
                    foreach (var p in animator.parameters)
                        if (p.type == AnimatorControllerParameterType.Bool && (p.name == "onGround" || p.name == "wakeup"))
                            animator.SetBool(p.nameHash, p.name == "onGround");
                    // Past the spawn / wake-up state the prefab starts in (in-game run 20261022-174939: kneeling figure
                    // on open) into the idle loop.
                    for (var i = 0; i < 4; i++) animator.Update(2f);
                }
                _worldLights.Clear();
                foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None))
                    if (l && l.type == LightType.Directional && !l.transform.IsChildOf(_rig.transform)) _worldLights.Add(l);
                _applied = -1;
                Builds++;
                Note("build");
                Adopt(before);
                return true;
            }
            catch (Exception e)
            {
                Adopt(before);
                Fail(e.ToString());
                Teardown("build failed");
                return false;
            }
        }

        private void BuildRig()
        {
            var camGo = new GameObject("RuneSkinPreviewCamera");
            camGo.transform.SetParent(_rig.transform, false);
            _camera = camGo.AddComponent<Camera>();
            _camera.enabled = false; // rendered manually (Render)
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.cullingMask = 1 << _layer;
            _camera.fieldOfView = Fov;
            _camera.nearClipPlane = 0.1f;
            _camera.farClipPlane = 30f;
            _camera.allowHDR = false;
            _camera.allowMSAA = true;
            _camera.useOcclusionCulling = false;
            _camera.depthTextureMode = DepthTextureMode.None;
            AddLight("Key", new Vector3(-0.45f, -0.55f, -1f), ThemeTokens.GetColor("color.inv.preview.key", new Color(1f, 0.93f, 0.82f)), ThemeTokens.GetFloat("scale.inv.preview.key", 1.15f));
            AddLight("Fill", new Vector3(0.7f, -0.15f, -1f), ThemeTokens.GetColor("color.inv.preview.fill", new Color(0.62f, 0.7f, 0.85f)), ThemeTokens.GetFloat("scale.inv.preview.fill", 0.45f));
            AddLight("Rim", new Vector3(0.2f, -0.35f, 1f), ThemeTokens.GetColor("color.inv.preview.rim", new Color(1f, 0.78f, 0.5f)), ThemeTokens.GetFloat("scale.inv.preview.rim", 0.7f));
        }

        private void AddLight(string name, Vector3 direction, Color color, float intensity)
        {
            var go = new GameObject("RuneSkinPreview" + name);
            go.transform.SetParent(_rig.transform, false);
            go.transform.rotation = Quaternion.LookRotation(direction.normalized);
            var l = go.AddComponent<Light>();
            l.type = LightType.Directional;
            l.color = color;
            l.intensity = intensity;
            l.shadows = LightShadows.None;
            l.cullingMask = 1 << _layer;
            l.renderMode = LightRenderMode.ForcePixel;
            l.enabled = false; // only while the preview camera renders (Render)
            _rigLights.Add(l);
        }

        private void MakeTexture(int w, int h)
        {
            FreeTexture();
            _rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32)
            {
                name = "RuneSkinPreview",
                antiAliasing = 4,
                useMipMap = false,
                autoGenerateMips = false
            };
            _rt.Create();
            LiveTextures++;
            if (_camera) _camera.targetTexture = _rt;
            _image.texture = _rt;
            _image.enabled = true;
        }

        private void FreeTexture()
        {
            if (_camera) _camera.targetTexture = null;
            if (_image) { _image.texture = null; _image.enabled = false; }
            if (!_rt) return;
            _rt.Release();
            Destroy(_rt);
            _rt = null;
            LiveTextures--;
        }

        private void Teardown(string why)
        {
            if (_clone || _rt || _rig) Note("teardown " + why);
            FreeTexture();
            if (_clone)
            {
                CollectRuntimeMaterials(_clone);
                LiveClones--;
            }
            foreach (var m in _owned)
                if (m && !SharedWithPlayer(m)) Destroy(m);
            _owned.Clear();
            foreach (var m in _ownedMeshes)
                if (m) Destroy(m);
            _ownedMeshes.Clear();
            if (_rig) Destroy(_rig);
            _rig = _clone = null;
            _vis = null;
            _camera = null;
            _player = null;
            _worldLights.Clear();
            _rigLights.Clear();
            _applied = -1;
        }

        // Materials instantiated on the clone's renderers (VisEquipment touches m_bodyModel.material(s); item scripts
        // may call Renderer.material): Unity does not free them with the GameObject. A " (Instance)" material that
        // the real player's renderers also use is shared, not ours, and is kept.
        private void CollectRuntimeMaterials(GameObject root)
        {
            var shared = new HashSet<Material>();
            var player = Player.m_localPlayer;
            if (player)
                foreach (var r in player.GetComponentsInChildren<Renderer>(true))
                    foreach (var m in r.sharedMaterials)
                        if (m) shared.Add(m);
            var mine = new HashSet<Material>();
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                foreach (var m in r.sharedMaterials)
                    if (m && m.name.EndsWith(" (Instance)", StringComparison.Ordinal) && !shared.Contains(m)) mine.Add(m);
            foreach (var m in mine)
                if (!_owned.Contains(m)) _owned.Add(m); // destroyed with the adopted ones (Teardown)
        }

        private void Fail(string why)
        {
            _failed = true;
            _placed = false;
            Plugin.Log.LogWarning($"[RuneSkin] character preview off (silhouette stays): {why}");
            if (gameObject.activeSelf) gameObject.SetActive(false);
        }

        // ------------------------------------------------------------------ per change / per frame

        private void Refresh()
        {
            _applied = _revision;
            if (!_vis || !_player) return;
            try
            {
                _player.SetupVisEquipment(_vis, false);
                _vis.UpdateVisuals();
                Strip(_clone, build: false);
                SetLayer(_clone.transform, _layer);
            }
            // The body materials were instanced at Build (VisEquipment's Renderer.material(s) getters reuse them,
            // VisEquipment.cs:636-700, :1025-1125); what a refresh can add are the new item instances' Renderer.material
            // copies ("(Instance)", on the clone): collected from it, no scene-wide FindObjectsOfTypeAll window (four full
            // object-table scans on every open after an equipment change).
            finally { CollectRuntimeMaterials(_clone); }
            Refreshes++;
        }

        // Material ownership by creation window: every material that appears while this component synchronously
        // builds or refreshes the clone (renderer.material instances of VisEquipment / item scripts, copies made with
        // new Material(...) that keep the source name) is the clone's; Unity never frees them with the GameObject
        // (in-game run 20261022-174939: +2 materials per build). Meshes the same way (MeshFilter.mesh /
        // SkinnedMeshRenderer instancing by item scripts: "<mesh> Instance" copies). Unity-internal (DontSave) ones are
        // left alone.
        private static HashSet<int> MaterialIds()
        {
            var ids = new HashSet<int>();
            foreach (var m in Resources.FindObjectsOfTypeAll<Material>())
                if (m) ids.Add(m.GetInstanceID());
            foreach (var m in Resources.FindObjectsOfTypeAll<Mesh>())
                if (m) ids.Add(m.GetInstanceID());
            return ids;
        }

        private void Adopt(HashSet<int> before)
        {
            var added = 0;
            var names = new List<string>();
            foreach (var m in Resources.FindObjectsOfTypeAll<Material>())
                if (m && !before.Contains(m.GetInstanceID()) && (m.hideFlags & HideFlags.DontSave) == HideFlags.None)
                {
                    _owned.Add(m);
                    added++;
                    if (names.Count < 8) names.Add(m.name);
                }
            foreach (var m in Resources.FindObjectsOfTypeAll<Mesh>())
                if (m && !before.Contains(m.GetInstanceID()) && (m.hideFlags & HideFlags.DontSave) == HideFlags.None && !_ownedMeshes.Contains(m))
                {
                    _ownedMeshes.Add(m);
                    added++;
                    if (names.Count < 8) names.Add("mesh " + m.name);
                }
            if (added > 0 && !_ownedLogged)
            {
                _ownedLogged = true;
                Plugin.Log.LogInfo($"[RuneSkin] character preview owns {added} runtime material(s) / mesh(es): {string.Join(", ", names)}");
            }
        }

        private static bool SharedWithPlayer(Material m)
        {
            var player = Player.m_localPlayer;
            if (!player) return false;
            foreach (var r in player.GetComponentsInChildren<Renderer>(true))
                foreach (var pm in r.sharedMaterials)
                    if (pm == m) return true;
            return false;
        }

        private void Frame()
        {
            _clone.transform.localRotation = Quaternion.Euler(0f, _yaw, 0f);
            var aspect = _rt.height > 0 ? (float)_rt.width / _rt.height : 1f;
            var d = PreviewMath.Distance(BodyH, SpanW, aspect, Fov, Pad, _share);
            _camera.aspect = aspect;
            _camera.farClipPlane = d + 10f; // a small figure share puts the camera far back
            _camera.transform.position = Origin + new Vector3(0f, CenterY, d);
            _camera.transform.rotation = Quaternion.LookRotation(Vector3.back);
            var clear = ThemeTokens.GetColor("color.inv.preview.clear", new Color(0f, 0f, 0f, 0f));
            if (_opaqueClear) clear.a = 1f;
            _camera.backgroundColor = clear;
        }

        // Time of day / weather independent: world directional lights off, rig lights on, fog off, flat ambient for this render only.
        private void Render()
        {
            var fog = RenderSettings.fog;
            var ambientMode = RenderSettings.ambientMode;
            var ambient = RenderSettings.ambientLight;
            var ambientIntensity = RenderSettings.ambientIntensity;
            _swapped.Clear();
            try
            {
                foreach (var l in _worldLights)
                    if (l && l.enabled && (l.cullingMask & (1 << _layer)) != 0)
                    {
                        l.enabled = false;
                        _swapped.Add(l);
                    }
                RenderSettings.fog = false;
                RenderSettings.ambientMode = AmbientMode.Flat;
                RenderSettings.ambientLight = ThemeTokens.GetColor("color.inv.preview.ambient", new Color(0.36f, 0.34f, 0.32f));
                RenderSettings.ambientIntensity = 1f;
                foreach (var l in _rigLights)
                    if (l) l.enabled = true;
                _camera.Render();
            }
            finally
            {
                foreach (var l in _rigLights)
                    if (l) l.enabled = false;
                foreach (var l in _swapped)
                    if (l) l.enabled = true;
                _swapped.Clear();
                RenderSettings.fog = fog;
                RenderSettings.ambientMode = ambientMode;
                RenderSettings.ambientLight = ambient;
                RenderSettings.ambientIntensity = ambientIntensity;
            }
        }

        // Once per session: shaders that write alpha 0 for opaque surfaces would leave the transparent-cleared texture
        // invisible on the RawImage; then the preview clears to an opaque theme colour instead.
        private void CheckAlpha()
        {
            if (_opaqueClear) return;
            var s = Sample();
            if (s.ColorCoverage > 0.01f && s.Coverage < s.ColorCoverage * 0.1f)
            {
                _opaqueClear = true;
                Plugin.Log.LogInfo($"[RuneSkin] character preview: shaders write no alpha (coverage {s.Coverage:0.000} vs colour {s.ColorCoverage:0.000}): opaque background");
            }
        }

        // Diagnostics (autotest, CheckAlpha): share of pixels with alpha, share differing from the clear colour, and a
        // coarse hash of the image. Reads the texture back (GPU stall): not for per-frame use.
        public PreviewSample Sample()
        {
            var result = new PreviewSample();
            if (!_rt) return result;
            var prev = RenderTexture.active;
            var tex = new Texture2D(_rt.width, _rt.height, TextureFormat.RGBA32, false);
            try
            {
                RenderTexture.active = _rt;
                tex.ReadPixels(new Rect(0, 0, _rt.width, _rt.height), 0, 0, false);
                tex.Apply(false);
                var px = tex.GetPixels32();
                var bg = (Color32)(_camera ? _camera.backgroundColor : Color.clear);
                int alpha = 0, color = 0, hash = 17;
                double luma = 0;
                for (var i = 0; i < px.Length; i++)
                {
                    var p = px[i];
                    if (p.a > 25) alpha++;
                    if (Math.Abs(p.r - bg.r) + Math.Abs(p.g - bg.g) + Math.Abs(p.b - bg.b) > 24)
                    {
                        color++;
                        luma += (0.2126 * p.r + 0.7152 * p.g + 0.0722 * p.b) / 255.0;
                    }
                    if (i % 61 == 0) hash = unchecked(hash * 31 + (p.r >> 4) * 256 + (p.g >> 4) * 16 + (p.b >> 4));
                }
                result.Coverage = px.Length > 0 ? (float)alpha / px.Length : 0f;
                result.ColorCoverage = px.Length > 0 ? (float)color / px.Length : 0f;
                result.Luma = color > 0 ? (float)(luma / color) : 0f;
                result.Hash = hash;
                result.Width = _rt.width;
                result.Height = _rt.height;
            }
            finally
            {
                RenderTexture.active = prev;
                Destroy(tex);
            }
            return result;
        }

        public struct PreviewSample
        {
            public float Coverage, ColorCoverage, Luma; // Luma: mean luminance of the drawn (non-background) pixels
            public int Hash, Width, Height;
        }

        // ------------------------------------------------------------------ drag

        public void OnBeginDrag(PointerEventData eventData) { }

        public void OnDrag(PointerEventData eventData)
        {
            if (eventData == null || eventData.button != PointerEventData.InputButton.Left) return;
            _yaw = PreviewMath.Yaw(_yaw, eventData.delta.x, DegPerPixel);
        }

        // ------------------------------------------------------------------ strip

        // Visual-only: everything but transforms, renderers, meshes, LOD groups, animators, particle systems, cloth
        // (Unity / MagicaCloth2), the clone's VisEquipment and MaterialMan's notifier goes; the clone's ZNetView stays
        // until activation (VisEquipment.Awake reads it; it then destroys itself). Removed in RequireComponent order,
        // so Unity never refuses (and logs) a removal.
        private static void Strip(GameObject root, bool build)
        {
            var pending = new List<Component>();
            foreach (var c in root.GetComponentsInChildren<Component>(true))
                if (c && !Keep(c, build)) pending.Add(c);
            for (var pass = 0; pass < 8 && pending.Count > 0; pass++)
            {
                var progress = false;
                for (var i = pending.Count - 1; i >= 0; i--)
                {
                    var c = pending[i];
                    if (!c) { pending.RemoveAt(i); continue; }
                    if (Required(c)) continue;
                    DestroyImmediate(c);
                    pending.RemoveAt(i);
                    progress = true;
                }
                if (!progress) break;
            }
            foreach (var c in pending)
                if (c is Behaviour b) b.enabled = false;
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
            }
        }

        private static bool Keep(Component c, bool build)
        {
            if (c is Transform || c is Renderer || c is MeshFilter || c is LODGroup || c is Animator || c is VisEquipment || c is MaterialManNotifier) return true;
            if (build && c is ZNetView) return true;
            var t = c.GetType();
            var ns = t.Namespace ?? "";
            return t.FullName == "UnityEngine.ParticleSystem" || t.FullName == "UnityEngine.Cloth" || ns.StartsWith("MagicaCloth2", StringComparison.Ordinal);
        }

        private static bool Required(Component c)
        {
            var type = c.GetType();
            foreach (var other in c.GetComponents<Component>())
            {
                if (!other || other == c) continue;
                foreach (RequireComponent req in other.GetType().GetCustomAttributes(typeof(RequireComponent), true))
                    if (Needs(req.m_Type0, type) || Needs(req.m_Type1, type) || Needs(req.m_Type2, type)) return true;
            }
            return false;
        }

        private static bool Needs(Type required, Type have) => required != null && required.IsAssignableFrom(have);

        private static void SetLayer(Transform t, int layer)
        {
            t.gameObject.layer = layer;
            for (var i = 0; i < t.childCount; i++) SetLayer(t.GetChild(i), layer);
        }
    }
}
