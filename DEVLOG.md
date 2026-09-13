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

## 2026-09-13 — 용병 성격(Personality) 시스템 추가 (전투 스탯 보정)

체형/패시브 등 후속 확장을 염두에 두고, 외형(`Characters`)이나 직업(`Mercenaries`)과는 별개로 `Assets/_Project/Scripts/Traits/` 아래 `GN3.Traits` 네임스페이스를 신설.

- `Personality` — 겁쟁이(Coward)/용맹(Brave)/냉정(Calm)/저돌적(Reckless)/신중(Cautious)/낙천(Cheerful) 6종 enum
- `PersonalityModifier` — 성격별 라벨/설명 + Attack/Defense/Health 배율(순수 데이터)
- `PersonalityTable` — 성격→보정치 테이블(static), `GetRandom(rng)`로 랜덤 선택, `Apply(baseStats, personality)`로 기본 스탯에 배율 적용해 최종 `CombatStats` 반환 (예: 겁쟁이 = 공격 -20%/방어 -10%, 저돌적 = 공격 +25%/방어 -15%)
- `Mercenary`에 `Personality` 프로퍼티 추가. `CurrentStats`가 기존 `MercenaryStatCalculator.Calculate(class, level)` 결과에 `PersonalityTable.Apply`를 한 번 더 거치도록 변경 (직업/레벨 기반 계산과 성격 보정을 분리 유지)
- `MercenaryMarketGenerator.Generate`가 용병 생성 시 시장과 같은 rng로 `PersonalityTable.GetRandom(rng)` 호출 → 외형과 마찬가지로 시드 고정 시 성격도 재현 가능
- `MercenaryMarketUI`/`PartyUI` 정보 텍스트에 `[성격라벨]` 표시 추가 (예: "카이런 전사 Lv.2 [겁쟁이] ATK 8 / DEF 4 / HP 30")
- (주의) `System.Random`과 `UnityEngine.Random`이 같이 `using`되면 `CS0104` 컴파일 에러가 났던 과거 이력(`CharacterPartLibrary` 참고)이 있어, `PersonalityTable`은 `using System;`을 빼고 `System.Random`/`System.Enum`을 명시적으로 사용
- 아직 미구현: 겁쟁이가 "전투 중 도망친다"처럼 스탯이 아닌 행동 자체에 영향을 주는 것은 `AutoBattleSimulator`에 별도 훅이 필요해 이번 범위에는 포함하지 않음(현재는 스탯 페널티로만 표현). 체형/패시브도 같은 `GN3.Traits` 네임스페이스에 이어서 추가 예정

## 2026-09-13 — 성격 7종 추가 (스탯형)

기존 6종에 이어 스탯 배율만으로 표현 가능한 성격 7종을 `Personality` enum과 `PersonalityTable.Modifiers`에 추가. 다른 코드(`GetRandom`, `Apply`, UI 표시)는 enum 전체를 순회하는 구조라 수정 없이 자동 반영됨.

- 다혈질(Hotblooded): 공격 +30% / 방어 -25%
- 침착함(Composed): 방어 +10% / 체력 +10%
- 비관적(Pessimistic): 공격 -15% / 방어 +20% / 체력 -10%
- 자신만만(Confident): 공격 +10% / 방어 +10%
- 무기력(Lethargic): 공격 -15% / 체력 -15%
- 예민함(Nervous): 공격 -10% / 방어 -10%
- 우직함(Steady): 체력 +25%

(보류) 분노/보호본능/저격수 기질/도박꾼/불굴처럼 전투 AI 행동 자체가 바뀌어야 하는 성격들과, 탐욕/게으름처럼 경제·컨디션 시스템이 필요한 성격들은 사용자 확인 후 이번 범위에서 제외. 나중에 패시브 시스템과 함께 `AutoBattleSimulator` 확장을 고려해 추가 예정.

## 2026-09-13 — 패시브 훅 시스템 (구조만, 구체 패시브는 미구현)

패시브는 성격(스탯 배율 고정값)과 달리 "전투 중 특정 시점에 발동"하는 게 많아서, 구체 패시브를 만들기 전에 `AutoBattleSimulator`가 외부 로직을 끼워넣을 수 있는 훅 지점부터 설계.

- `Combat/AttackContext.cs` — 한 번의 공격에 대한 가변 컨텍스트(`Attacker`/`Defender`/`Round`/`Damage`/`Evaded`/`Critical`). 패시브가 `Damage`, `Evaded`, `Critical`을 직접 수정해서 전투 흐름에 개입
- `Combat/PassiveBase.cs` — 패시브 추상 기반 클래스. 필요한 훅만 override하면 되도록 전부 `virtual`/기본 구현 제공:
  - `OnBattleStart(self, allies, enemies)` — 전투 시작 시 1회
  - `OnRoundStart(self, round)` — 매 라운드 시작 시
  - `OnAttack(context)` — 공격자 기준, 데미지 계산 직후(치명타 등)
  - `OnDefend(context)` — 방어자 기준, 데미지 적용 직전(회피/피해감소 등)
  - `OnDamageDealt(context)` — 공격자 기준, 데미지 적용 직후(흡혈 등)
  - `OnAllyDefeated(self, defeatedAlly)` — 같은 팀 유닛이 쓰러졌을 때
  - `OnLethalDamage(context)` — 자신이 죽는 상황일 때 호출, `true` 반환 시 체력 1로 생존
- `Combatant`에 `Passives` 리스트 추가(기본 빈 리스트, 기존 생성자 호출부는 전부 그대로 동작)
- `AutoBattleSimulator.Simulate`에 훅 호출 지점 배선: 전투 시작(`RaiseBattleStart`) → 라운드 시작(`RaiseRoundStart`) → 공격마다 `OnAttack`→`OnDefend`→(치명상이면 `OnLethalDamage` 판정)→데미지 적용→`OnDamageDealt`→(처치 시)`OnAllyDefeated`
- 아직 실제 패시브 구현체(치명타/회피/흡혈 등)는 없음. `Mercenary`에도 아직 `Passives`를 연결하지 않음 — 다음 단계는 A그룹(체력 비율 조건부 스탯)부터 이 파이프라인 위에 구체 패시브로 얹는 것

## 2026-09-13 — 이동속도(MoveSpeed) 스탯 + 회피 확률, 암살자 직업 추가

패시브 만들기 전 선행 작업으로, 이동속도가 빠를수록 공격을 회피할 확률이 올라가는 기본 전투 스탯을 추가. 패시브가 아니라 모든 유닛에 적용되는 코어 매커니즘으로 구현(치명타/흡혈 같은 개별 패시브와 달리, 회피 판정은 `AutoBattleSimulator`가 매 공격마다 기본으로 수행).

- `CombatStats`에 `MoveSpeed` 필드 추가 (기존 생성자 호출부는 전부 `moveSpeed = 0` 기본값으로 하위호환)
- `CombatFormulas.CalculateEvadeChance(defender)` 추가 — `MoveSpeed × 2% (EvadeChancePerMoveSpeed)`, 최대 60%(`MaxEvadeChance`)로 캡. 상대방 스탯과 무관하게 자신의 이동속도만으로 결정되는 단순 공식
- `AutoBattleSimulator.Simulate`에서 `OnAttack`/`OnDefend` 패시브 훅 이후, 데미지 적용 전에 `_random.NextDouble() < CalculateEvadeChance(target)`로 회피 판정 → 회피 시 `AttackContext.Evaded = true`, 데미지 미적용
- `BattleEvent`에 `Evaded` 필드 추가, 로그의 `Damage`도 회피 시 0으로 기록되도록 수정. `PartyUI`/`BattleTestRunner` 로그 출력에 "(회피)" 표시 추가
- `MercenaryClassSO`에 `BaseMoveSpeed`/`MoveSpeedGrowth` 필드 추가, `MercenaryStatCalculator.Calculate`가 레벨에 따라 계산해 `CombatStats.MoveSpeed`에 반영
- `EnemySquadGenerator`도 난이도 기반으로 적 이동속도 생성(`4 + difficulty + rng.Next(-1,2)`) — 플레이어만 회피하는 일방적인 구조가 되지 않도록 적도 동일 매커니즘 적용
- 기존 3직업(Warrior/Archer/Healer) asset에 이동속도 값 보강: 전사 4(+0/lv), 힐러 6(+1/lv), 궁수 8(+1/lv) — 육중한 순서로 낮게
- **암살자(Assassin) 직업 신설** (`Resources/MercenaryClasses/Assassin.asset`): 공격 15(+3/lv) / 방어 2(+1/lv) / 체력 22(+3/lv) / 이동속도 14(+2/lv) — 공격력 최상위 + 압도적 이동속도로 "때리기 전에 안 맞는" 컨셉. 레벨1 기준 회피 확률 약 28%(14×2%)
- 시장/파티 카드 정보 텍스트에 `SPD`/`속` 표시 추가

(참고) 회피는 방어자의 이동속도만으로 결정되는 절대값 공식이라, 공격자 이동속도는 아직 관여하지 않음. 나중에 "상대적 속도차" 방식으로 바꾸고 싶으면 `CalculateEvadeChance`에 attacker 파라미터를 추가하면 됨.

## 2026-09-13 — 직업별 패시브 4종 (전사/궁수/힐러/암살자)

패시브 훅 시스템 위에 직업 특색이 드러나는 패시브를 하나씩 얹음. `MercenaryClassSO`에 직업 종류를 나타내는 `Kind`(enum) 필드를 추가해 클래스 이름 문자열이 아니라 enum으로 패시브를 매칭하도록 함.

- `Mercenaries/MercenaryClassKind.cs` — `Warrior/Archer/Healer/Assassin` enum. `MercenaryClassSO.Kind` 필드로 노출, 기존 4개 asset(Warrior/Archer/Healer/Assassin)에 `Kind: 0~3` 값 설정
- `Mercenaries/ClassPassiveFactory.cs` — `MercenaryClassKind` → 패시브 인스턴스 리스트 매핑 static 팩토리. `Mercenary.ToCombatant()`가 전투용 `Combatant` 생성 시 호출해서 자동으로 직업 패시브를 붙임
- `Combat/Passives/` 폴더에 구체 패시브 4개 추가 (전부 `PassiveBase` 상속):
  - **전사 — 불굴의 방벽**: `OnDefend`에서 체력 30% 이하일 때 받는 피해 25% 감소
  - **궁수 — 정밀 사격**: `OnAttack`에서 25% 확률로 치명타(피해 2배, `context.Critical = true`)
  - **힐러 — 치유의 기운**: `OnBattleStart`에서 아군 목록을 캐싱해두고, `OnRoundStart`마다 아군 중 체력 비율이 가장 낮은 유닛을 최대체력의 10% 회복 (`Combatant.Heal` 신규 추가)
  - **암살자 — 기습**: `OnAttack`에서 대상이 아직 피해를 입지 않은(체력 100%) 상태면 피해 +50%
- `PassiveBase`에 `Description` 추상 프로퍼티 추가(성격의 `PersonalityModifier.Description`과 동일한 역할)
- `AttackContext`에 `Rng`(전투 시드와 동일한 `System.Random`) 필드 추가 — 패시브의 확률 판정(치명타 등)도 전투 시드에 종속되어 재현 가능하도록 함. `AutoBattleSimulator`가 `AttackContext` 생성 시 자신의 `_random`을 전달
- 시장/파티 카드 정보 텍스트에 `<패시브 이름>` 태그 추가
- (버그 수정) `PersonalityTable.Apply`가 새 `CombatStats`를 만들 때 `moveSpeed`를 넘기지 않아 성격 보정을 거치면 이동속도가 0으로 리셋되던 문제 발견 → `moveSpeed: baseStats.MoveSpeed`로 그대로 전달하도록 수정 (성격은 이동속도에 영향 주지 않음)

## 2026-09-13 — 직업별 레어 패시브 4종 (등장 확률 낮춤)

기존 직업 패시브(불굴의 방벽/정밀 사격/치유의 기운/기습)와 별개로, 더 강력하지만 등장 확률이 낮은 "레어 패시브"를 직업마다 하나씩 추가.

- `ClassPassiveFactory.RarePassiveChance = 0.15f` — 용병 생성 시 일반 패시브 대신 레어 패시브를 받을 확률. `RollRare(rng)`로 판정
- `Mercenary.HasRarePassive` (성격/외형과 동일하게 생성 시 1회 결정되어 고정되는 값). `MercenaryMarketGenerator.Generate`가 시장 rng로 `ClassPassiveFactory.RollRare(rng)`를 호출해 결정 → 시드 고정 시 재현 가능
- `ClassPassiveFactory.Create(kind, rare)`로 시그니처 변경 — `rare=true`면 아래 레어 패시브를, 아니면 기존 일반 패시브를 반환. **주의**: 시장/파티 카드가 패시브 이름을 보여주려고 매번 `Create`를 새로 호출하므로, 반드시 `merc.HasRarePassive`(생성 시 확정된 값)를 넘겨야 실제 전투(`ToCombatant()`)와 UI 표시가 일치함 — 매번 다시 굴리면 표시와 실제가 어긋나는 버그가 되므로 이 값을 캐시하는 구조로 설계함
- **전사 — 광역 도발**: `Combatant.IsTaunting`(신규 필드) 사용. 매 라운드 시작 시 20% 확률로 이번 라운드 동안 자신에게 `IsTaunting = true`. `AutoBattleSimulator.PickTarget`이 적 목록에 도발 중인 유닛이 있으면 최우선으로 타겟팅 → 사실상 "이번 라운드 모든 적의 공격이 자신에게 쏠림"
- **궁수 — 광역 공격**: 매 턴 20% 확률로 `TryTriggerAoeAttack`이 true 반환 → 시뮬레이터가 단일 타겟 대신 살아있는 적 전원에게 개별 `ResolveAttack` 수행(각각 회피/치명타 등 정상 판정)
- **힐러 — 아군 부활**: `OnBattleStart`에서 아군 목록 캐싱, 전투 중 아직 안 썼고 쓰러진 아군이 있으면 매 라운드 30% 확률로 그중 1명을 최대체력 50%로 부활(`Combatant.Heal`이 체력 0인 유닛에도 그대로 동작해 재사용). 전투당 1회로 제한(`_used` 플래그)
- **암살자 — 즉사**: 공격 시 12% 확률로 `context.Damage`를 999999로 고정 — 이후 상대 패시브의 퍼센트 감소를 거쳐도 확정적으로 처치되도록 충분히 큰 값 사용. 단, 회피 판정은 그대로 통과하므로 이동속도가 빠른 대상은 즉사도 회피 가능
- **구조 변경**: `AutoBattleSimulator`의 데미지 적용 파이프라인을 `ResolveAttack(attacker, target, round, targetTeam, log)` 메서드로 추출 — 기존 단일 타겟 공격과 광역 공격(궁수 레어)이 동일한 로직(패시브 훅/회피/치명상 판정/로그/아군사망 알림)을 공유하도록 리팩터링
- `PassiveBase.OnRoundStart`에 `System.Random rng` 파라미터 추가(도발/부활의 라운드별 확률 판정에 필요), `TryTriggerAoeAttack(rng)` 훅 신규 추가. 기존 `HealingAuraPassive`도 새 시그니처에 맞춰 수정
- 시장/파티 카드의 `<패시브 이름>` 태그에 레어일 경우 `(레어)` 표시 추가

## 2026-09-13 — 성격/패시브 태그에 마우스오버 툴팁 추가

- `UI/TooltipUI.cs` — 전역 툴팁 싱글턴. 최초 호출 시 별도의 `TooltipCanvas`(ScreenSpaceOverlay, sortingOrder 1000, `DontDestroyOnLoad`)를 스스로 만들어서 씬 구조와 무관하게 동작. `Show(text)`/`Hide()`, 활성화 중에는 매 프레임 마우스 위치를 따라다님
- `UI/TooltipTrigger.cs` — `IPointerEnterHandler`/`IPointerExitHandler` 구현. 텍스트 오브젝트에 붙이면 호버 시 `TooltipUI.Instance.Show(Text)`, 벗어나면 `Hide()`. `OnDisable`에서도 `Hide()` 호출 → 호버 중인 행이 파괴돼도(고용 버튼 클릭 등) 툴팁이 화면에 남지 않도록 함
- `MercenaryMarketUI.CreateCard`/`PartyUI.CreateMemberRow`: 기존에 하나의 문자열로 합쳐져 있던 "이름/직업/레벨 + [성격] + <패시브> + 스탯" 텍스트를 각각 별도의 `Text` 오브젝트로 분리(`CreateTaggedLabel` 헬퍼 추가) — `[성격]`과 `<패시브>` 태그에만 `TooltipTrigger`를 붙여서 `PersonalityModifier.Description`/`PassiveBase.Description`을 호버 설명으로 표시
- `PartyUI`는 패널 폭이 좁아 한 줄에 다 넣기 어려워서, 이름+태그를 담는 `TopRow`(HorizontalLayoutGroup)와 스탯 텍스트를 담는 `InfoColumn`(VerticalLayoutGroup)으로 한 단계 더 중첩한 구조로 변경. 시장 카드는 폭이 넉넉해 한 줄 그대로 유지
- 태그 텍스트는 `horizontalOverflow = Overflow` + `LayoutElement.flexibleWidth = 0`으로 설정해 내용 길이에 맞는 자연스러운 폭을 갖도록 함(레이아웃 그룹이 Text 자체의 preferred width를 사용)
- (버그 수정 1차 시도) 툴팁이 커서를 안 따라가고 화면 중앙에 고정되던 문제 → `RectTransform.position`(월드 좌표)을 직접 대입하던 방식이 원인으로 추정하여 `RectTransformUtility.ScreenPointToLocalPointInRectangle` + `anchoredPosition` 방식으로 변경했으나 실제로는 효과 없었음(아래 진짜 원인 참고)

## 2026-09-13 — 툴팁 진짜 원인 발견: New Input System 전용 프로젝트에서 레거시 Input 사용

패널 크기 문제와 함께 재요청받은 "툴팁이 아직도 중앙 고정" 이슈의 진짜 원인을 찾음.

- `ProjectSettings/ProjectSettings.asset`의 `activeInputHandler: 1` 확인 → 이 프로젝트는 **New Input System 전용**(레거시 Input Manager 비활성화)으로 설정되어 있음. `Packages/manifest.json`에도 `com.unity.inputsystem` 설치되어 있음을 확인
- 이 설정에서는 `UnityEngine.Input.mousePosition` 같은 레거시 `Input` API 호출이 매 프레임 예외를 던짐 → `TooltipUI.UpdatePosition()`의 좌표 계산이 실제로는 한 번도 성공적으로 실행되지 않아 `anchoredPosition`이 항상 기본값(0,0=캔버스 중앙)에 머물러 있었던 것이 진짜 원인. 이전 커밋의 "로컬 좌표 변환" 수정은 방향은 맞았지만 애초에 `Input.mousePosition` 자체가 예외를 던져 도달하지 못했음
- `TooltipUI.cs`를 `UnityEngine.InputSystem.Mouse.current.position.ReadValue()`로 교체(레거시 `Input` 참조 제거). `Mouse.current == null` 방어 코드 추가
- 프로젝트에 `.asmdef`가 없어 전부 기본 Assembly-CSharp으로 컴파일되므로 `UnityEngine.InputSystem` 참조에 별도 조치 불필요

## 2026-09-13 — 패널 크기 확대 + 상호 배타적 활성화

- **패널 크기 확대** (`Assets/Scenes/MainScene.unity` 직접 수정, UnityMCP 연결 불가로 씬 YAML을 텍스트로 편집):
  - MarketPanel, QuestPanel: 900×680 → **1200×780** (중앙 앵커, anchoredPosition은 0,0 그대로 유지)
  - PartyPanel: 480×680 → **640×780** (우상단 앵커 유지, 모서리에서 20px 여백을 유지하도록 anchoredPosition을 (-260,-360)→(-340,-410)으로 재계산)
  - 원인: 최근 성격/패시브 태그를 카드 안에서 별도 `Text` 요소로 분리하면서 한 행에 들어가는 요소가 늘었는데 패널 폭은 그대로였음 → `ScrollListWrapper`가 씌운 `RectMask2D` 뷰포트 밖으로 밀려난 텍스트(스탯, 버튼 등)가 잘려서 "글자가 안 보이는" 현상으로 나타난 것. 내부 여백(`ScrollListWrapper.Wrap` offset, 버튼 anchoredPosition 등)은 전부 패널 가장자리 기준 마진이라 패널 자체를 키우는 것만으로 코드 수정 없이 해결됨
  - `MercenaryMarketUI`/`PartyUI`/`QuestBoardUI`의 패널 크기 주석도 새 값으로 갱신
- **패널 상호 배타적 활성화**: `PanelActivator`에 `RegisterGroup(params GameObject[])` 추가, 그룹으로 등록된 패널 중 하나를 `Open()`하면 나머지는 자동으로 `SetActive(false)`. `MainMenuBootstrapper.Bootstrap()`에서 `PanelActivator.RegisterGroup(marketPanel, partyPanel, questPanel)` 호출 → 메뉴 버튼(용병시장/파티 구성/퀘스트)과 퀘스트 수락 시 자동으로 열리는 파티 패널 모두 이 그룹을 통해 열리므로, 한 창을 열면 켜져 있던 다른 창이 자동으로 닫힘. 각 패널의 개별 "X" 닫기 버튼은 기존처럼 자기 자신만 닫음(변경 없음)

## 2026-09-13 — 전투 결과를 코루틴으로 순차 재생 + 퀘스트별 예상 소요시간

기존엔 `AutoBattleSimulator.Simulate()` 결과가 나오자마자 최근 10턴 로그를 텍스트로 한 번에 출력했음. 이제 전투 계산 자체는 그대로 즉시 수행하되(결정론적 시뮬레이션은 그대로 두고), **연출만** 시간을 두고 순차적으로 보여주도록 변경.

- `Quest.EstimatedDurationSeconds` 추가 — `3 + 난이도×1.5 + 처치수×0.5`(초)로 계산되는 예상 임무 완료 시간. 난이도/처치 수가 높을수록 전투 연출이 길어짐
- `QuestBoardUI` 퀘스트 카드에 "예상 소요시간 n초" 표시 추가
- `PartyUI.StartBattle()`: 시뮬레이션은 즉시 실행해 `BattleResult`를 미리 확보하고, 화면 표시는 `PlayBattle` 코루틴으로 위임. 전투 시작 버튼은 재생 중엔 `interactable = false`로 잠가서 중복 클릭 방지
- `PlayBattle(quest, result)` 코루틴: `quest.EstimatedDurationSeconds`를 로그 이벤트 수로 나눠 이벤트당 대기시간을 계산하고, `WaitForSeconds`로 한 줄씩 순차 공개. 화면에는 최근 8줄만 유지(`LinkedList` 기반 롤링 윈도우)해서 텍스트가 무한히 길어지지 않게 함. 재생이 끝나면 기존과 동일하게 "임무 완료/실패" + `BuildResultSummary`(최근 10턴) 최종 요약을 표시하고 `EndQuest()` 호출
- (알려진 한계, 해결됨 — 아래 항목 참고) 재생 도중 다른 패널을 열어 PartyPanel이 `SetActive(false)`되면 Unity가 코루틴을 그 자리에서 중단시킴

## 2026-09-13 — 패널을 닫아도 전투 재생이 계속되도록 수정

Unity는 `GameObject.SetActive(false)`가 호출되면 그 오브젝트에 붙어 있던 컴포넌트의 코루틴을 즉시 중단시킨다. `PartyUI.StartCoroutine(PlayBattle(...))`이 PartyUI 자신(=PartyPanel의 자식)에서 실행되고 있었기 때문에, 다른 패널을 열어 PartyPanel이 비활성화되면 전투 재생이 끊겼음.

- `UI/CoroutineHost.cs` 추가 — `TooltipUI`와 동일한 패턴(최초 접근 시 `DontDestroyOnLoad`로 자체 GameObject 생성)의 항상 활성 상태인 코루틴 실행 전용 싱글턴
- `PartyUI.StartBattle()`이 `this.StartCoroutine` 대신 `CoroutineHost.Instance.StartCoroutine(PlayBattle(...))`을 사용하도록 변경 → 패널이 닫혀도 코루틴은 별도의 항상-활성 오브젝트에서 계속 실행됨
- 코루틴은 여전히 `PartyUI` 인스턴스의 `RenderResultText`/`EndQuest` 등을 호출해 `resultText.text`를 갱신하는데, 이는 GameObject가 비활성 상태여도 문제없이 동작함(단지 화면에 안 보일 뿐, 패널을 다시 열면 이미 갱신된 텍스트가 바로 보임)
- (참고) 씬을 재로드/언로드하는 기능이 아직 없는 프로젝트라, "재생 도중 씬이 파괴되는" 경우의 방어 코드는 넣지 않음 — 필요해지면 그때 추가

## 2026-09-13 — 용병 이름 풀 확장 + 중복 방지

기존 이름 풀이 판타지풍 16개뿐이라 시장을 몇 번만 새로고침해도 이름이 자주 겹쳤음.

- `MercenaryNamePool`에 서부권 이름 20개(잭/윌리엄/엘리자베스 등), 한국 이름 20개(지훈/서연 등) 추가 — 기존 판타지 이름 16개 + 신규 40개 = 총 56개
- 한 번 뽑힌 이름은 `_usedNames`(static `HashSet<string>`)에 기록되어, 풀이 소진되기 전까지 다시 뽑히지 않음. 시장 새로고침 때마다(고용 여부와 무관하게) 소비되며, 56개를 모두 소진하면 자동으로 초기화되어 다시 순환
- 이름 소비 시점이 "시장에 표시될 때"이기 때문에, 굳이 고용하지 않아도 새로고침을 반복하면 이름이 계속 새로 나옴(기존에 사용자가 겪던 반복 문제를 직접 해결)

## 2026-09-13 — 일본 판타지풍 이름 추가

`MercenaryNamePool`에 일본풍 이름 20개(하야토/렌/사쿠라/유이 등) 추가 — 판타지 16 + 서부 20 + 한국 20 + 일본 20 = 총 76개. 중복 방지 로직(`_usedNames`)은 그대로 전체 풀을 대상으로 동작하므로 추가 코드 변경 없이 자동 반영됨.

## 2026-09-13 — 이름 중복 방지 기준을 "고용 중" 여부로 변경

기존엔 "시장에 한 번 표시되면" 영구적으로 이름을 소비했는데(고용 여부 무관), 요청에 따라 "고용한 용병의 이름만 재등장 방지, 해고하면 다시 등장 가능"으로 의미를 바꿈.

- `MercenaryNamePool.GetRandom`에서 static `_usedNames`(영구 소비 기록) 제거. 대신 `GetRandom(rng, excluded)`로 시그니처 변경 — 매 호출마다 외부에서 넘겨준 제외 목록만 참고하는 순수 함수로 단순화(내부 상태 없음)
- `MercenaryMarketGenerator.Generate`가 매 새로고침마다 `PlayerParty.Instance.Members`의 이름을 제외 목록으로 구성해서 `GetRandom`에 전달 → 현재 파티에 있는(고용 중인) 용병과 이름이 겹치지 않음. 같은 배치 안에서 뽑힌 이름도 즉시 제외 목록에 추가해 한 새로고침 내 중복도 방지
- 고용하지 않고 새로고침만 반복하면 이전에 보였던(고용 안 한) 이름은 다시 등장할 수 있음(의도된 동작 — "고용한" 이름만 막는 것이 이번 요청 사항). 파티에서 해고하면 그 순간부터 `Members`에서 빠지므로 다음 새로고침부터 그 이름이 다시 등장 가능
