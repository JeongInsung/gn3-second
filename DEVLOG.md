# 개발 로그

작업 내역을 날짜별로 기록합니다. 새 작업을 할 때마다 맨 아래에 날짜별 항목을 추가해주세요.

## 2026-09-03 — 프로젝트 초기 세팅

- Unity 프로젝트 초기 커밋, URP 2D 템플릿 기반 설정 (`Initial commit`, `1`, `2`)
- `Assets/Scenes/SampleScene.unity` → `MainScene.unity`로 이름 변경
- 폴더 구조 추가: `Assets/정인성`, `Assets/조정식`, `예비씬.unity` (팀원별 작업 공간으로 추정)
- `PreScene.unity` 씬 추가

## 2026-09-05 — 용병 시장 / 전투 시스템 / 파티 구성 (커밋 `2852b8f`)

`Assets/_Project/Scripts` 아래에 코드 구조를 새로 만들고 핵심 게임플레이 시스템 3종을 구현.

### Combat (`GN3.Combat` 네임스페이스)
- `CombatStats` — Attack/Defense/MaxHealth를 담는 순수 데이터 클래스
- `Combatant` — 이름 + 스탯 + 현재 체력을 가진 전투 유닛. `TakeDamage`, `IsAlive` 제공
- `CombatFormulas` — 데미지 계산식 (공격력 - 방어력, 최소 데미지 1 보장)
- `BattleEvent` — 한 턴의 공격 로그(라운드, 공격자, 대상, 데미지, 처치 여부)
- `BattleResult` — 전투 결과(승패, 라운드 수, 로그, 양 팀 명단)
- `AutoBattleSimulator` — 자동 전투 시뮬레이터. 팀A/팀B를 번갈아가며 턴 진행, 랜덤 타겟팅, 최대 라운드 제한, 승패 판정
- `EnemySquadGenerator` — 난이도(difficulty)에 따라 적 스쿼드를 랜덤 생성 (이름은 고블린/오크/슬라임 등 8종 풀에서 선택)
- `BattleTestRunner` — 임의의 전사/궁수 vs 고블린/오크 파티로 시뮬레이터를 테스트해보는 MonoBehaviour (씬에 붙여서 콘솔 로그로 확인)

### Mercenaries (`GN3.Mercenaries` 네임스페이스)
- `MercenaryClassSO` — ScriptableObject 기반 용병 직업 정의 (레벨1 기본 스탯 + 레벨당 성장치). `Assets/_Project/Resources/MercenaryClasses/`에 Warrior/Archer/Healer 3종 에셋 존재
- `MercenaryStatCalculator` — 직업 + 레벨로 현재 스탯 계산 (레벨업 보너스 = 성장치 × (레벨-1))
- `Mercenary` — 개별 용병 인스턴스(고유 ID, 이름, 직업, 레벨). `ToCombatant()`로 전투용 유닛 변환
- `MercenaryNamePool` — 용병 이름 랜덤 풀 (카이런, 브렌, 이바 등 16종)
- `MercenaryMarketGenerator` — 직업 풀에서 랜덤 레벨의 용병 목록 생성
- `MercenaryMarket` — 시장 상태 관리(목록 크기, 레벨 범위). `Refresh()`로 매물 새로고침
- `PlayerParty` — 싱글턴. 파티원 목록(최대 인원 제한), 전투 참가/제외(active) 상태 관리, `OnChanged` 이벤트로 UI 갱신 트리거, `ToCombatants()`로 활성 파티원만 전투 유닛 변환

### UI (`GN3.UI` 네임스페이스)
- `MercenaryMarketUI` — 용병 시장 화면. 코드로 UI 로우를 동적 생성(이름/직업/레벨/스탯 표시 + 고용 버튼), 새로고침 버튼 지원
- `PartyUI` — 파티 관리 화면. 파티원 목록 표시, 전투 참가/제외 토글, 해고 버튼, "전투 시작" 버튼으로 `EnemySquadGenerator` + `AutoBattleSimulator`를 실행하고 결과 요약(최근 10턴 로그)을 텍스트로 출력

> 참고: 현재 UI는 코드로 `GameObject`를 생성해 임시로 구성한 상태(플레이스홀더 스타일)로, 정식 UI 아트/레이아웃 적용 전 단계.

## 2026-09-05 — 디자인 씬 (커밋 `49bbf6e`)

- `Assets/Scenes/DesignScene.unity` 씬 추가. UI/아트 디자인 작업용 씬으로 추정 (씬 자체는 Unity 에디터에서 열어서 확인 필요 — 텍스트 diff만으로는 내용 파악 어려움)

## 2026-09-05 — 용병 시장/파티 패널에 캐릭터 초상화 슬롯 추가 (1차: 직업 단위)

- `MercenaryClassSO`에 `Portrait` (Sprite) 필드 추가 — 직업별 초상화 지정용
- `MercenaryMarketUI.CreateCard`, `PartyUI.CreateMemberRow`에 정보 텍스트 왼쪽으로 정사각형 `Image` 슬롯(`CreatePortrait`) 추가 (시장 카드 48px, 파티 로우 40px)
- → 이후 개별 용병 단위 파츠 조합 방식으로 대체됨 (아래 항목 참고)

## 2026-09-05 — 파츠 조합 기반 개별 캐릭터 외형 시스템

직업 단위 초상화 대신, 용병 한 명 한 명이 서로 다른 외형을 갖도록 파츠 조합 시스템으로 교체.

- `Assets/_Project/Resources/CharacterParts/{Head,Body,Arm,Leg,Weapon}/` 폴더 생성 — 각 폴더에 스프라이트(png/aseprite/psd)를 넣으면 `Resources.LoadAll`로 자동 인식되어 랜덤 풀에 포함됨 (코드 수정 불필요)
- `GN3.Characters` 네임스페이스 신설 (`Assets/_Project/Scripts/Characters/`)
  - `CharacterPartCategory` — Head/Body/Arm/Leg/Weapon enum
  - `CharacterPartLibrary` — 카테고리별 `Resources.LoadAll<Sprite>` 결과를 캐싱하고 랜덤 1개를 뽑아주는 static 클래스
  - `CharacterAppearance` — 5개 파츠 스프라이트 조합을 담는 데이터. `GenerateRandom(Random rng)`로 한 번에 생성
- `Mercenary`에 `Appearance` 프로퍼티 추가 (인스턴스 생성 시 고정되어 고용 후에도 외형 유지)
- `MercenaryMarketGenerator.Generate`가 용병을 만들 때마다 `CharacterAppearance.GenerateRandom(rng)`도 같이 생성해서 전달 (시장 seed와 동일한 rng를 공유하므로 시드 고정 시 외형도 재현 가능)
- `MercenaryMarketUI`, `PartyUI`의 초상화 슬롯을 레이어 방식으로 변경 — Body → Leg → Arm → Weapon → Head 순으로 5장을 겹쳐 그림. 아직 없는 파츠는 투명 처리되어 안 보이고, 슬롯 배경만 옅은 회색으로 표시됨
- `MercenaryClassSO.Portrait` 필드 제거 (더 이상 사용 안 함)
- (버그 수정) `CharacterPartLibrary`, `CharacterAppearance`에서 `System.Random`과 `UnityEngine.Random`이 이름이 겹쳐 `CS0104` 컴파일 에러 발생 → `System.Random`으로 명시해서 해결

## 2026-09-05 — 메인 화면 메뉴 버튼 + 패널 열기/닫기

- `Assets/_Project/Scripts/UI/MainMenuBootstrapper.cs` 추가. `[RuntimeInitializeOnLoadMethod(AfterSceneLoad)]`로 씬 로드 직후 자동 실행되는 정적 부트스트랩 (씬 파일을 직접 편집하지 않고 코드로만 UI를 주입하는 기존 프로젝트 컨벤션을 따름)
- 씬에서 이름으로 `MarketPanel`, `PartyPanel`, 그리고 `Canvas`를 찾아서:
  - 좌상단에 "용병시장" / "파티 구성" 버튼 바 생성 → 각각 누르면 해당 패널 `SetActive(true)`
  - 각 패널 우상단에 "X" 닫기 버튼 생성 → 누르면 해당 패널만 `SetActive(false)`
  - 부트스트랩 마지막에 두 패널을 `SetActive(false)`로 초기 비활성화
- 두 패널은 서로 배타적이지 않음 (동시에 열어둘 수 있음) — 필요하면 한쪽 열 때 다른 쪽 자동으로 닫게 바꿀 수 있음
- 참고: `MarketPanel`/`PartyPanel`/`Canvas` 이름에 의존하는 방식이라 씬에서 해당 오브젝트 이름이 바뀌면 콘솔에 경고가 뜨고 버튼이 생성되지 않음

## 2026-09-05 — 용병 리스트 스크롤 추가

- `Assets/_Project/Scripts/UI/ScrollListWrapper.cs` 추가 — 기존 `listContainer`(이미 `VerticalLayoutGroup`+`ContentSizeFitter`로 세로로 늘어나게 되어있던 리스트)를 `RectMask2D`+`ScrollRect`가 붙은 뷰포트로 감싸서 스크롤 가능하게 만듦. 씬을 직접 편집하지 않고 런타임에 계층을 재구성하는 방식
- `MercenaryMarketUI`, `PartyUI` 둘 다 `Awake()` 시작 부분에서 `ScrollListWrapper.Wrap(listContainer)` 호출하도록 연결 — 시장/파티 목록 둘 다 스크롤 적용
- (참고) 레벨업 시 파티 최대 인원을 늘리는 기능은 아직 레벨업 시스템 자체가 없어서 보류 — 스크롤만 우선 작업함

## 2026-09-05 — 스크롤 방식을 실제 스크롤바(오른쪽 드래그)로 재작업

기존 방식(뷰포트에 직접 드래그)은 두 가지 문제가 있었음: 1) content 앵커를 바꾸면서 기존 sizeDelta.x가 새 앵커 기준으로 재해석되어 리스트 폭이 틀어짐, 2) 눈에 보이는 스크롤바가 없어서 "인터넷처럼 오른쪽 바 잡고 스크롤"하는 조작이 불가능했음.

- `ScrollListWrapper`를 표준 Unity ScrollView 구조(`ScrollView`(ScrollRect) → `Viewport`(RectMask2D) → content, 그리고 `Scrollbar`(Handle 포함))로 재작성
- content를 새 앵커로 옮길 때 `sizeDelta.x`를 0으로 명시 고정해서 폭이 틀어지던 버그 수정
- 오른쪽에 14px 너비의 스크롤바 트랙 + 핸들 추가, `ScrollRect.verticalScrollbarVisibility = Permanent`로 항상 보이게 설정 → 마우스로 바를 직접 잡고 드래그 가능

## 2026-09-05 — 스크롤 뷰 위치/크기 버그 수정 (UnityMCP로 직접 진단)

UnityMCP가 재연결되어 에디터에 직접 붙어서 진단. 실제 원인 3가지를 찾아 수정:

1. **뷰 영역이 0 높이로 고정되던 문제**: `ScrollListWrapper.Wrap`이 리스트(content)의 "행이 없을 때 0으로 접힌" 앵커/사이즈 값을 그대로 ScrollView 뷰포트 크기로 복사하고 있었음 — ScrollView에는 `ContentSizeFitter`가 없어서 이 0 값이 영원히 고정됨. → 패널 실제 레이아웃(제목/버튼 위치)을 직접 측정해서, 고정된 `offsetMin/offsetMax` 영역을 명시적으로 지정하도록 변경 (`Wrap(content, offsetMin, offsetMax)`). 시장: 제목/새로고침 버튼 아래~패널 하단, 파티: 제목/전투 버튼 아래~결과 텍스트 위.
2. **content의 sizeDelta 오염**: content를 top-anchor로 바꾸면서 이전(꽉채우기 anchor 기준) sizeDelta 값을 그대로 남겨둬서 높이가 음수(-680)로 계산되던 버그 → `sizeDelta = Vector2.zero`로 초기화 후 `LayoutRebuilder.ForceRebuildLayoutImmediate` 호출.
3. **패널이 비활성 상태일 때 리스트 높이가 계산되지 않는 문제**: `MercenaryMarketUI`/`PartyUI`는 Canvas에 붙어있어 항상 `Start()`가 실행되지만, `MainMenuBootstrapper`가 그 직후 패널을 비활성화해버려서 이후 아무리 `ForceRebuildLayoutImmediate`를 호출해도 유니티가 비활성 오브젝트의 레이아웃은 계산하지 않아 리스트 높이가 계속 0으로 남음. → `MainMenuBootstrapper.OpenPanel`에서 패널을 활성화한 직후, 패널 하위의 모든 `ContentSizeFitter`를 찾아 각각 직접 `ForceRebuildLayoutImmediate`를 호출하도록 수정 (부모 기준으로 호출하면 중첩된 스크롤 콘텐츠까지 반영되지 않는 것도 확인함).

플레이 모드에서 버튼 클릭을 실제로 실행시켜 확인: 시장 리스트 846×360(뷰포트 590), 파티 리스트 426×0(멤버 없어서 정상) — 의도한 위치/크기로 정상 동작 확인.

## 2026-09-05 — 퀘스트 목록 (몬스터 처치, 랜덤 생성)

시장과 같은 방식으로 랜덤 콘텐츠 생성. 보상 시스템은 아직 없음(다음 단계).

- `GN3.Quests` 네임스페이스 (`Assets/_Project/Scripts/Quests/`)
  - `Quest` — 제목, 대상 몬스터 이름, 처치 수, 난이도를 담는 데이터
  - `QuestGenerator` — 몬스터 이름 풀(고블린/오크/슬라임 등, `EnemySquadGenerator`와 동일 목록)에서 랜덤 조합해 퀘스트 1개 생성
  - `QuestBoard` — `MercenaryMarket`과 동일한 패턴의 목록 관리자, `Refresh()`로 매물 새로고침
- `Assets/_Project/Scripts/UI/QuestBoardUI.cs` — `MercenaryMarketUI`와 동일한 패턴으로 카드 리스트 생성, `ScrollListWrapper` 적용
- **이번엔 UnityMCP로 에디터에 직접 붙어서 씬을 만듦** (런타임 코드로만 조립하다가 스크롤 버그를 겪었던 지난 경험 때문에): `MarketPanel`을 통째로 복제해 `QuestPanel` 생성 → 제목 텍스트를 "퀘스트"로 변경 → Canvas에 `QuestBoardUI` 컴포넌트 추가 후 `listContainer`/`refreshButton` 필드를 씬의 실제 오브젝트로 연결
  - 복제 시 자식 오브젝트들의 `anchoredPosition`이 에디터가 자동으로 살짝 밀어놓는 현상이 있어서(Title -14px, RefreshButton -23px 등) 원본 값으로 되돌려 보정함
- `MainMenuBootstrapper`에 "퀘스트" 메뉴 버튼 + 닫기 버튼 추가 (`MarketPanel`/`PartyPanel`과 동일한 방식)
- 플레이 모드에서 버튼 클릭 실행까지 직접 확인: 퀘스트 5개 랜덤 생성, 리스트/뷰포트 크기 정상(846×360, 뷰포트 590) — 시장 패널과 동일하게 정상 동작

## 2026-09-05 — 퀘스트 수락 → 파티 패널 전투 시작 연결

기존 파티 패널의 "전투 시작" 버튼(항상 고정으로 존재)을 없애고, 퀘스트를 수락해야만 전투를 시작할 수 있도록 변경.

- `PartyUI`에서 정적 `battleButton`/`enemyCount`/`enemyDifficulty` 필드 제거. 대신 `public void ActivateQuest(Quest quest)`를 추가 — 호출되면 그 퀘스트의 `EnemyCount`/`Difficulty`를 기억하고, 패널 우상단(기존 버튼과 같은 위치)에 "전투 시작" 버튼을 동적으로 생성함(이미 생성돼 있으면 재사용)
- `StartBattle()`은 이제 활성화된 퀘스트가 없으면 "진행 중인 퀘스트가 없습니다" 안내만 하고 아무 것도 하지 않음
- `QuestBoardUI`의 각 퀘스트 카드에 "수락" 버튼 추가 → 클릭 시 `PartyUI.ActivateQuest(quest)` 호출 + `PartyPanel` 열기(닫혀있던 패널 활성화) + 해당 퀘스트 카드는 시장의 "고용"과 동일하게 목록에서 제거
- 패널 활성화 + ScrollRect 레이아웃 재계산 로직을 `MainMenuBootstrapper`와 `QuestBoardUI`가 공통으로 쓰도록 `PanelActivator.Open(panel)` 정적 헬퍼로 분리
- 씬에서 기존 `PartyPanel/BattleButton` 오브젝트 삭제, `QuestBoardUI.partyPanel` 필드를 `PartyPanel`로 연결 (UnityMCP로 직접 편집)
- 플레이 모드에서 전체 흐름 실행 확인: 시장에서 용병 고용 → 파티 참가 → 퀘스트 수락(파티 패널 자동으로 열림 + 전투 시작 버튼 생김) → 전투 시작까지 에러 없이 동작

## 2026-09-05 — 퀘스트 승패 판정 + 전투 버튼 무한 반복 버그 수정

- `PartyUI.StartBattle()`에서 전투 결과(`BattleOutcome`)에 따라 결과 텍스트 맨 앞에 "임무 완료"(TeamAVictory) 또는 "임무 실패"(그 외 — 파티 전멸/무승부)를 표시
- 전투가 끝나면 `EndQuest()`를 호출해 `_activeQuest`를 비우고 전투 시작 버튼을 파괴함 → 버튼을 계속 눌러도 전투가 무한 반복되던 문제 해결 (퀘스트를 다시 수락해야 다음 전투 가능)
- 플레이 모드에서 리플렉션으로 내부 상태까지 직접 확인: 전투 후 `_activeQuest == null`, `_battleButtonGO == null`, 결과 텍스트에 "임무 실패"/전투 로그 정상 표시
