using GN3.CharacterAnim;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace GN3.UI
{
    /// <summary>
    /// DesignScene 전용 부트스트랩. 씬 파일을 편집하지 않고 코드로만 UI/캐릭터를 주입하는 프로젝트 컨벤션을 따른다.
    /// (MainMenuBootstrapper와 같은 방식이지만 활성 씬이 DesignScene일 때만 동작해 MainScene에는 영향이 없다.)
    /// "랜덤 캐릭터" 버튼을 누르면 파츠 시트의 1프레임을 조합해 캐릭터를 새로 만든다.
    /// </summary>
    public static class DesignSceneBootstrapper
    {
        private const string TargetSceneName = "DesignScene";

        private static readonly System.Random Rng = new System.Random();
        private static CharacterPartView _view;
        private static Text _infoText;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (SceneManager.GetActiveScene().name != TargetSceneName) return;

            var designerGO = new GameObject("CharacterDesigner", typeof(CharacterPartView));
            // 파츠 pivot이 바닥 중앙(PPU 64 → 프레임 높이 4유닛)이라 화면 중앙보다 조금 아래에 발을 둔다.
            designerGO.transform.position = new Vector3(0f, -2f, 0f);
            _view = designerGO.GetComponent<CharacterPartView>();

            var canvas = CreateCanvas();
            EnsureEventSystem();
            CreateMenuButton(canvas.transform, "랜덤 캐릭터", GenerateRandomCharacter);
            _infoText = CreateInfoText(canvas.transform);

            GenerateRandomCharacter();
        }

        private static void GenerateRandomCharacter()
        {
            var composed = RandomCharacterComposer.Compose(Rng);
            _view.Show(composed);

            if (composed.Parts.Count == 0)
            {
                _infoText.text = "파츠를 찾지 못했습니다. Resources/CharacterAnim 폴더와 characters.txt를 확인하세요.";
                return;
            }

            _infoText.text =
                $"몸(torso/팔/다리): {composed.BodyCharacter}\n" +
                $"머리(얼굴+머리카락): {composed.SourceOf(CharacterPartId.Head)}";
        }

        private static Canvas CreateCanvas()
        {
            var canvasGO = new GameObject("DesignCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = canvasGO.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            // MainScene의 Canvas 설정과 동일 (1920x1080 기준 스케일)
            var scaler = canvasGO.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            return canvas;
        }

        private static void EnsureEventSystem()
        {
            if (Object.FindFirstObjectByType<EventSystem>() != null) return;
            // 프로젝트가 New Input System 전용이라 StandaloneInputModule 대신 InputSystemUIInputModule을 쓴다.
            new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
        }

        private static void CreateMenuButton(Transform parent, string label, UnityEngine.Events.UnityAction onClick)
        {
            var buttonGO = new GameObject(label + "Button", typeof(RectTransform), typeof(Image), typeof(Button));
            buttonGO.transform.SetParent(parent, false);

            var rect = buttonGO.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(16f, -16f);
            rect.sizeDelta = new Vector2(160f, 44f);

            buttonGO.GetComponent<Image>().color = new Color(0.2f, 0.2f, 0.25f, 0.9f);

            var text = CreateText(buttonGO.transform, label, 18, TextAnchor.MiddleCenter);
            var textRect = text.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;

            buttonGO.GetComponent<Button>().onClick.AddListener(onClick);
        }

        private static Text CreateInfoText(Transform parent)
        {
            var text = CreateText(parent, "", 16, TextAnchor.UpperLeft);
            var rect = text.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(16f, -72f);
            rect.sizeDelta = new Vector2(480f, 120f);
            return text;
        }

        private static Text CreateText(Transform parent, string content, int fontSize, TextAnchor alignment)
        {
            var go = new GameObject("Text", typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);

            var text = go.GetComponent<Text>();
            text.text = content;
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.color = Color.white;
            return text;
        }
    }
}
