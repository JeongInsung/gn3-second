using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GN3.Mercenaries;
using GN3.Quests;
using UnityEditor;
using UnityEngine;

namespace GN3.EditorTools
{
    /// <summary>
    /// 부상·질병 편집 창(에디터 전용). 메뉴: GN3/부상·질병 편집기
    /// 왼쪽은 Resources/Ailments 의 AilmentSO 목록(새로 만들기·복제·삭제), 오른쪽은 고른 에셋의 Inspector + 효과·발생 확률 미리보기.
    /// Play 중이면 아래에 파티 용병마다 걸기·치료·진행도 조절 칸이 나온다(병원 입원도 바로 반영).
    /// </summary>
    public class AilmentEditorWindow : EditorWindow
    {
        private const string Folder = "Assets/_Project/Resources/" + AilmentCatalog.ResourcesFolder;
        private const float ListWidth = 230f;
        private const double RepaintInterval = 0.3;

        private List<AilmentSO> _assets = new List<AilmentSO>();
        private AilmentSO _selected;
        private Editor _editor;
        private string _search = "";
        private string _renameInput = "";
        private Vector2 _listScroll, _editScroll;
        private double _nextRepaint;

        [MenuItem("GN3/부상·질병 편집기")]
        public static void Open() => GetWindow<AilmentEditorWindow>("부상·질병 편집기");

        private void OnEnable()
        {
            Reload();
            EditorApplication.update += TickRepaint;
            EditorApplication.projectChanged += Reload;
        }

        private void OnDisable()
        {
            EditorApplication.update -= TickRepaint;
            EditorApplication.projectChanged -= Reload;
            if (_editor != null) DestroyImmediate(_editor);
        }

        // Play 중엔 진행도가 계속 바뀌므로 자주 다시 그린다.
        private void TickRepaint()
        {
            if (!Application.isPlaying || EditorApplication.timeSinceStartup < _nextRepaint) return;
            _nextRepaint = EditorApplication.timeSinceStartup + RepaintInterval;
            Repaint();
        }

        private void Reload()
        {
            _assets = AssetDatabase.FindAssets("t:" + nameof(AilmentSO), new[] { Folder })
                .Select(guid => AssetDatabase.LoadAssetAtPath<AilmentSO>(AssetDatabase.GUIDToAssetPath(guid)))
                .Where(a => a != null)
                .OrderBy(a => a.category).ThenByDescending(a => a.isSevere).ThenBy(a => a.name)
                .ToList();
            if (_selected == null || !_assets.Contains(_selected)) Select(_assets.FirstOrDefault());
            AilmentCatalog.Reload();
            Repaint();
        }

        private void Select(AilmentSO asset)
        {
            _selected = asset;
            _renameInput = asset != null ? asset.name : "";
            if (_editor != null) DestroyImmediate(_editor);
            _editor = asset != null ? Editor.CreateEditor(asset) : null;
        }

        private void OnGUI()
        {
            EditorGUILayout.BeginHorizontal();
            DrawList();
            DrawEditor();
            EditorGUILayout.EndHorizontal();
        }

        // ---------- 왼쪽: 목록 ----------

        private void DrawList()
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(ListWidth));
            _search = EditorGUILayout.TextField(_search, EditorStyles.toolbarSearchField);

            _listScroll = EditorGUILayout.BeginScrollView(_listScroll);
            if (_assets.Count == 0)
            {
                EditorGUILayout.HelpBox($"{Folder} 에 부상·질병이 없습니다.", MessageType.Info);
                if (GUILayout.Button("기본 4종 만들기")) CreateDefaults();
            }
            foreach (var asset in _assets)
            {
                if (!string.IsNullOrEmpty(_search) && !asset.displayName.Contains(_search) && !asset.name.Contains(_search)) continue;
                var rect = EditorGUILayout.GetControlRect(false, 22f);
                if (asset == _selected) EditorGUI.DrawRect(rect, new Color(0.24f, 0.37f, 0.59f, 0.6f));
                EditorGUI.DrawRect(new Rect(rect.x + 4f, rect.y + 5f, 12f, 12f), asset.color);
                string label = $"{asset.displayName}  [{(asset.IsInjury ? "부상" : "질병")}]{(asset.isSevere ? " 중함" : "")}";
                GUI.Label(new Rect(rect.x + 22f, rect.y + 2f, rect.width - 22f, rect.height), label);
                if (Event.current.type == EventType.MouseDown && rect.Contains(Event.current.mousePosition))
                {
                    GUI.FocusControl(null); // 이름 칸에 입력 중이던 글자가 다음 에셋에 남지 않게
                    Select(asset);
                    Event.current.Use();
                }
            }
            EditorGUILayout.EndScrollView();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("새 부상")) CreateNew(AilmentCategory.Injury);
            if (GUILayout.Button("새 질병")) CreateNew(AilmentCategory.Illness);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(_selected == null))
            {
                if (GUILayout.Button("복제")) Duplicate(_selected);
                if (GUILayout.Button("삭제")) Delete(_selected);
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }

        // ---------- 오른쪽: 편집 ----------

        private void DrawEditor()
        {
            EditorGUILayout.BeginVertical();
            _editScroll = EditorGUILayout.BeginScrollView(_editScroll);
            if (_selected == null || _editor == null)
            {
                EditorGUILayout.HelpBox("왼쪽에서 부상·질병을 고르거나 새로 만드세요.", MessageType.Info);
            }
            else
            {
                DrawRename();
                EditorGUILayout.Space(4f);
                _editor.OnInspectorGUI();
                EditorGUILayout.Space(8f);
                DrawPreview(_selected);
                if (Application.isPlaying)
                {
                    EditorGUILayout.Space(8f);
                    DrawPlayTest(_selected);
                }
            }
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private void DrawRename()
        {
            EditorGUILayout.BeginHorizontal();
            _renameInput = EditorGUILayout.TextField("에셋 이름(저장 id)", _renameInput);
            using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(_renameInput) || _renameInput == _selected.name))
            {
                if (GUILayout.Button("이름 바꾸기", GUILayout.Width(80f))
                    && EditorUtility.DisplayDialog("에셋 이름 바꾸기",
                        $"'{_selected.name}' → '{_renameInput}'\n저장 파일은 이 이름으로 상태이상을 기억합니다. 예전 저장에서 이 상태이상을 앓던 용병은 불러올 때 낫게 됩니다.", "바꾸기", "취소"))
                {
                    string error = AssetDatabase.RenameAsset(AssetDatabase.GetAssetPath(_selected), _renameInput.Trim());
                    if (!string.IsNullOrEmpty(error)) Debug.LogError($"[부상·질병] 이름을 바꾸지 못했습니다: {error}");
                    Reload();
                }
            }
            EditorGUILayout.EndHorizontal();
        }

        private static void DrawPreview(AilmentSO def)
        {
            EditorGUILayout.LabelField("미리보기", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(def.EffectText(), MessageType.None);

            var lines = new List<string>();
            if (def.Has(AilmentTrigger.AfterBattle) || def.Has(AilmentTrigger.AfterAmbush))
            {
                string where = string.Join("·", new[] { def.Has(AilmentTrigger.AfterBattle) ? "전투" : null, def.Has(AilmentTrigger.AfterAmbush) ? "습격" : null }.Where(s => s != null));
                lines.Add($"{where} 뒤 체력 10% 남음 {Pct(Ailments.FightChance(def, 0.1f))} · 30% {Pct(Ailments.FightChance(def, 0.3f))} · 50% {Pct(Ailments.FightChance(def, 0.5f))} · 80% {Pct(Ailments.FightChance(def, 0.8f))}");
            }
            if (def.Has(AilmentTrigger.ExpeditionDaily) || def.Has(AilmentTrigger.VillageDaily))
            {
                string where = string.Join("·", new[] { def.Has(AilmentTrigger.ExpeditionDaily) ? "파견 중" : null, def.Has(AilmentTrigger.VillageDaily) ? "마을" : null }.Where(s => s != null));
                lines.Add($"{where} 하루: 맑음 {Pct(Ailments.DailyChance(def, false, false, false))} · 비 {Pct(Ailments.DailyChance(def, true, false, false))} · 눈 {Pct(Ailments.DailyChance(def, false, true, false))} · 지친 용병 {Pct(Ailments.DailyChance(def, false, false, true))}");
            }
            if (lines.Count == 0) lines.Add("발생 조건(triggers)이 없어 저절로는 걸리지 않습니다(전염·시험으로만).");
            if (def.contagionPerDay > 0f) lines.Add($"전염: 입원 안 한 마을 동료 한 명마다 하루 {Pct(def.contagionPerDay)}");

            float hospitalHours = 1f / (Hospital.ProgressPerHour * def.hospitalSpeed);
            string natural = def.noNaturalRecovery || def.naturalSpeed <= 0f ? "저절로는 안 나음"
                : $"마을에서 저절로 약 {1f / (Ailments.NaturalProgressPerHour * def.naturalSpeed):0}시간";
            lines.Add($"완치: 병원 약 {hospitalHours:0}시간(치료비 약 {Mathf.CeilToInt(hospitalHours * Hospital.FeePerHour)}G) · {natural}");
            EditorGUILayout.HelpBox(string.Join("\n", lines), MessageType.Info);
            EditorGUILayout.HelpBox("Play 중에 고친 값도 에셋에 그대로 남습니다.", MessageType.None);
        }

        private static string Pct(float p) => $"{p * 100f:0.#}%";

        // ---------- Play 중: 용병에게 시험 ----------

        private static void DrawPlayTest(AilmentSO selected)
        {
            EditorGUILayout.LabelField("용병에게 시험 (Play 중)", EditorStyles.boldLabel);
            var members = PlayerParty.Instance.Members.Where(m => m.IsAlive).ToList();
            if (members.Count == 0)
            {
                EditorGUILayout.HelpBox("파티에 용병이 없습니다.", MessageType.Info);
                return;
            }

            bool changed = false;
            foreach (var merc in members)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.BeginHorizontal();
                string where = ExpeditionLog.Instance.IsOnExpedition(merc) ? " · 파견 중" : Hospital.IsAdmitted(merc) ? " · 입원 중" : TrainingHall.IsTraining(merc) ? " · 훈련 중" : HotSpring.IsBathing(merc) ? " · 온천 중" : "";
                EditorGUILayout.LabelField($"{merc.Name}  (체력 {merc.CurrentHealth}/{merc.CurrentStats.MaxHealth}){where}", EditorStyles.boldLabel);
                bool sameCategory = selected.IsInjury ? merc.HasInjury : merc.HasIllness;
                using (new EditorGUI.DisabledScope(sameCategory))
                {
                    if (GUILayout.Button(new GUIContent($"{selected.displayName} 걸기", sameCategory ? "같은 갈래(부상/질병)를 이미 앓고 있습니다" : ""), GUILayout.Width(110f)))
                        changed |= Ailments.Inflict(merc, selected, "[편집기] 상태이상에 걸렸다");
                }
                using (new EditorGUI.DisabledScope(!merc.HasAilment))
                {
                    if (GUILayout.Button("모두 치료", GUILayout.Width(70f)))
                    {
                        foreach (var ailment in merc.Ailments.ToList()) merc.RemoveAilment(ailment);
                        changed = true;
                    }
                }
                EditorGUILayout.EndHorizontal();

                foreach (var ailment in merc.Ailments.ToList())
                {
                    EditorGUILayout.BeginHorizontal();
                    float progress = EditorGUILayout.Slider($"  {ailment.Def.displayName} 진행도", ailment.Progress * 100f, 0f, 99f) / 100f;
                    if (!Mathf.Approximately(progress, ailment.Progress)) ailment.Progress = progress;
                    if (GUILayout.Button("치료", GUILayout.Width(50f)))
                    {
                        merc.RemoveAilment(ailment);
                        changed = true;
                    }
                    EditorGUILayout.EndHorizontal();
                }
                EditorGUILayout.EndVertical();
            }
            if (changed) Hospital.AdmitWaiting(); // 걸면 바로 입원, 다 나으면 다음 시간에 퇴원
        }

        // ---------- 만들기·복제·삭제 ----------

        private void CreateNew(AilmentCategory category)
        {
            var asset = CreateInstance<AilmentSO>();
            asset.category = category;
            if (category == AilmentCategory.Illness)
            {
                asset.displayName = "새 질병";
                asset.color = new Color(0.45f, 0.75f, 0.35f);
                asset.triggers = AilmentTrigger.ExpeditionDaily;
                asset.chance = 0.02f;
                asset.contagionPerDay = 0.1f;
            }
            else
            {
                asset.displayName = "새 부상";
                asset.healthBelow = 0.5f;
                asset.scaleWithMissingHealth = true;
            }
            Save(asset, category == AilmentCategory.Illness ? "NewIllness" : "NewInjury");
        }

        private void Duplicate(AilmentSO source)
        {
            string path = AssetDatabase.GenerateUniqueAssetPath(AssetDatabase.GetAssetPath(source));
            if (!AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(source), path)) return;
            var copy = AssetDatabase.LoadAssetAtPath<AilmentSO>(path);
            copy.displayName += " (복사본)";
            EditorUtility.SetDirty(copy);
            AssetDatabase.SaveAssets();
            Reload();
            Select(copy);
        }

        private void Delete(AilmentSO asset)
        {
            if (!EditorUtility.DisplayDialog("부상·질병 삭제",
                    $"'{asset.displayName}'({asset.name})을(를) 지울까요?\n저장 파일에서 이 상태이상을 앓던 용병은 불러올 때 낫게 됩니다.", "삭제", "취소")) return;
            AssetDatabase.DeleteAsset(AssetDatabase.GetAssetPath(asset));
            _selected = null;
            Reload();
        }

        /// <summary>타박상·골절·감기·열병을 처음 값으로 다시 만든다(폴더가 비었을 때).</summary>
        private void CreateDefaults()
        {
            Save(Make("타박상", AilmentCategory.Injury, false, new Color(0.9f, 0.6f, 0.25f), a =>
            {
                a.attackPenalty = 0.1f; a.chance = 0.6f; a.healthBelow = 0.6f; a.scaleWithMissingHealth = true;
            }), "Bruise", false);
            Save(Make("골절", AilmentCategory.Injury, true, new Color(0.95f, 0.35f, 0.25f), a =>
            {
                a.attackPenalty = 0.25f; a.defensePenalty = 0.25f; a.moveSpeedPenalty = 0.1f;
                a.chance = 0.5f; a.healthBelow = 0.25f; a.scaleWithMissingHealth = true; a.hospitalSpeed = 0.5f; a.naturalSpeed = 0.5f;
            }), "Fracture", false);
            Save(Make("감기", AilmentCategory.Illness, false, new Color(0.45f, 0.75f, 0.35f), a =>
            {
                a.attackPenalty = 0.1f; a.defensePenalty = 0.1f; a.triggers = AilmentTrigger.ExpeditionDaily;
                a.chance = 0.028f; a.rainBonus = a.snowBonus = a.exhaustedBonus = 0.042f; a.contagionPerDay = 0.15f;
            }), "Cold", false);
            Save(Make("열병", AilmentCategory.Illness, true, new Color(0.75f, 0.4f, 0.8f), a =>
            {
                a.attackPenalty = 0.2f; a.defensePenalty = 0.2f; a.triggers = AilmentTrigger.ExpeditionDaily;
                a.chance = 0.012f; a.rainBonus = a.snowBonus = a.exhaustedBonus = 0.018f; a.contagionPerDay = 0.25f; a.moraleLossPerDay = 2;
            }), "Fever", false);
            Reload();
        }

        private static AilmentSO Make(string displayName, AilmentCategory category, bool severe, Color color, Action<AilmentSO> setup)
        {
            var asset = CreateInstance<AilmentSO>();
            asset.displayName = displayName;
            asset.category = category;
            asset.isSevere = severe;
            asset.color = color;
            setup(asset);
            return asset;
        }

        private void Save(AilmentSO asset, string fileName, bool select = true)
        {
            if (!AssetDatabase.IsValidFolder(Folder))
            {
                Directory.CreateDirectory(Folder);
                AssetDatabase.Refresh();
            }
            string path = AssetDatabase.GenerateUniqueAssetPath($"{Folder}/{fileName}.asset");
            AssetDatabase.CreateAsset(asset, path);
            AssetDatabase.SaveAssets();
            if (!select) return;
            Reload();
            Select(asset);
        }
    }
}
