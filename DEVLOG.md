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

## 2026-09-15 — DesignScene 랜덤 캐릭터 생성 (파츠 시트 1프레임 조합)

pixel-anim-tool로 만든 캐릭터 파츠 스프라이트 시트를 게임에 넣는 첫 단계. **MainScene은 건드리지 않고 DesignScene에서만** 동작하며, 아직 애니메이션은 없고 각 시트의 **프레임 0(rest pose)** 만 잘라 조합한다.

- 자산: `Downloads\저장\저장\<캐릭터>\parts_<clip>_sheets\` → `Assets/_Project/Resources/CharacterAnim/<캐릭터>/<clip>/`로 복사 (`clip` = idle/walk/slash/slash2). 캐릭터 4개(`갈_짧_남_작업복`, `금 조끼`, `금_짧_갈조끼`, `붉 보통 로브`), 파츠 PNG(256×256 프레임 가로 1행) + `manifest.json`(z-order/fps 등) 그대로 포함. 툴 내부 리그 데이터인 최상위 `<캐릭터>.json`은 복사 안 함
- `Scripts/Editor/CharacterAnimImporter.cs` (Editor 전용 `AssetPostprocessor`)
  - `Resources/CharacterAnim/` 아래 PNG를 자동으로 Sprite(Multiple) / Point 필터 / 무압축 / PPU 64 / maxTextureSize 4096으로 임포트하고, PNG 헤더에서 폭을 읽어 256×256 셀로 슬라이스 (`{파츠}_{NN}` 이름, pivot 바닥 중앙). Unity 6에서 `TextureImporter.spritesheet`가 obsolete라 `ISpriteEditorDataProvider`(com.unity.2d.sprite) 사용
  - Resources는 디렉터리를 나열할 수 없어서, 임포트 때마다 캐릭터 폴더 목록을 `Resources/CharacterAnim/characters.txt`로 자동 생성 (메뉴 `GN3/CharacterAnim/캐릭터 목록 재생성`으로 수동 실행도 가능). 새 캐릭터는 폴더만 넣으면 인식됨
- `Scripts/CharacterAnim/` (`GN3.CharacterAnim` 네임스페이스, 기존 `GN3.Characters`의 정적 5파츠 시스템과는 별개)
  - `CharacterPartId` — 파츠 이름 상수 + 몸 세트(torso/arm_f/arm_b/leg_f/leg_b) / 독립 파츠(head/hair/weapon) 구분
  - `PartClipManifest` — manifest.json 매핑(`JsonUtility`), z-order 기본값 폴백
  - `CharacterAnimLibrary` — `characters.txt` 파싱, `Resources.LoadAll<Sprite>`로 파츠 프레임 로드/캐싱(이름 접미 인덱스로 정렬), manifest 로드
  - `RandomCharacterComposer.Compose(rng, clip="slash")` — **몸 세트는 한 캐릭터 폴더에서 함께**, head/hair/weapon은 각각 폴더 무관 독립 랜덤. z-order는 몸 세트 캐릭터의 manifest 사용. slash 클립을 쓰는 이유: 프레임 0이 idle과 같은 rest pose이면서 무기가 포함된 유일한 클립. `slash_fx`는 이펙트라 제외(프레임 0이 완전 투명)
  - `CharacterPartView` — 파츠별 `SpriteRenderer` 자식을 (0,0)에 두고 sortingOrder만 z-order 순서로 부여 (모든 시트가 같은 좌표계라 별도 오프셋 불필요). 이후 애니메이션 단계에서 프레임 인덱스만 바꾸면 되도록 설계
- `Scripts/UI/DesignSceneBootstrapper.cs` — `MainMenuBootstrapper`와 같은 `RuntimeInitializeOnLoadMethod` 패턴이지만 **활성 씬 이름이 `DesignScene`일 때만** 동작. Canvas(1920×1080 스케일) + EventSystem(`InputSystemUIInputModule`) + 좌상단 "랜덤 캐릭터" 버튼 + 조합 정보 텍스트를 코드로 생성하고, `CharacterDesigner` 오브젝트(위치 0,-2)에 캐릭터를 그림. 시작 시 1회 자동 생성. 씬 파일(`DesignScene.unity`)은 편집하지 않음
- 검증: UnityMCP 미연결 상태라 Roslyn(csc)으로 Assets 전체 52파일을 Unity 참조 DLL과 함께 컴파일해 에러 0 확인. 실제 임포트/플레이는 에디터에서 확인 필요
- (참고) `MainMenuBootstrapper`는 DesignScene에서 패널을 못 찾아 경고 1줄을 남기지만 무해하므로 그대로 둠

## 2026-09-15 — 랜덤 캐릭터 소스 클립을 slash → idle로 변경

- `CharacterAnimLibrary.DefaultClip`을 `"idle"`로 변경. 사용자 요청대로 **idle 폴더의 시트 프레임 0만** 사용. idle 폴더에는 `weapon.png`/`slash_fx.png`가 없으므로 무기 없는 rest pose 캐릭터가 나옴 (`RandomCharacterComposer`가 `HasPart`로 존재하는 파츠만 뽑아 추가 수정 없이 동작)
- `DesignSceneBootstrapper` 조합 정보 텍스트에서 "무기" 줄 제거

## 2026-09-15 — 용병시장/파티 초상화를 파츠 조합 캐릭터로 교체

DesignScene에서 검증한 파츠 조합 캐릭터를 MainScene의 시장 카드/파티 로우 초상화 슬롯에 프로필처럼 표시. 씬 파일은 편집하지 않음.

- `Mercenary.Appearance` 타입을 `GN3.Characters.CharacterAppearance` → `GN3.CharacterAnim.ComposedCharacter`로 교체. `MercenaryMarketGenerator.Generate`가 시장 rng로 `RandomCharacterComposer.Compose(rng)`를 호출해 생성 시 외형 확정(시드 재현성 유지, 고용 후 파티에서도 같은 얼굴)
- `UI/CharacterPortraitUI.cs` 추가 — 시장/파티 양쪽에서 재사용하는 공용 초상화 위젯(프리팹 역할). 파츠 시트가 256×256 캔버스에서 캐릭터를 작게 담고 있어(x 104~158, y 74~198) 슬롯을 `RectMask2D`로 마스킹하고 레이어를 확대·이동해 크롭. 프리셋 `Bust`(중심 (131,106), 84px — 머리~가슴, 기본값) / `FullBody`(중심 (131,136), 136px). 레이어는 `ComposedCharacter.ZOrderBackToFront` 순서로 `Image` 생성, 파츠 없으면 배경만 표시
- `MercenaryMarketUI`(48px)/`PartyUI`(40px)의 중복 `CreatePortrait`/`AddPortraitLayer`를 제거하고 `CharacterPortraitUI.Create` 호출로 교체
- 구 시스템 삭제: `Scripts/Characters/`(CharacterAppearance/CharacterPartLibrary/CharacterPartCategory)와 비어 있던 `Resources/CharacterParts/` — 새 시스템이 완전히 대체
- 검증: Roslyn 컴파일 에러 0, `GN3.Characters` 참조 0건. 실제 표시는 에디터에서 MainScene Play로 확인 필요

## 2026-09-16 — 캐릭터 폴더 드롭 자동 임포트

새 캐릭터를 만들 때마다 수동으로 복사/폴더명 변경하던 것을 없앰. `CharacterAnimImporter` 확장.

- **사용법**: pixel-anim-tool "💾 저장" 결과 폴더(예: `Downloads\저장\저장\귀족옷\`)를 **그대로** `Assets/_Project/Resources/CharacterAnim/`에 복사하고 Unity 창을 클릭하면 끝. `parts_{clip}_sheets` 폴더는 `{clip}`으로 자동 정리(`AssetDatabase.MoveAsset`), PNG는 기존대로 자동 슬라이스, `characters.txt`가 갱신되어 시장/DesignScene에 바로 등장
- 정리 작업은 임포트 중에 `MoveAsset`을 부를 수 없어서 `OnPostprocessAllAssets`에서 `EditorApplication.delayCall`로 한 프레임 미룸(중복 예약 방지 플래그). 같은 이름의 `{clip}` 폴더가 이미 있으면 경고만 하고 건너뜀
- 최상위 `<캐릭터>.json`(툴 리그 데이터)은 그대로 둠 — Resources는 요청 시에만 로드하므로 부담 없음
- 메뉴 `GN3/CharacterAnim/저장 폴더에서 캐릭터 가져오기...`: 폴더 선택 창에서 툴 저장 폴더를 고르면 하위 캐릭터 전부를 복사(PNG/manifest.json만, 덮어쓰기) 후 Refresh. 마지막 경로는 `EditorPrefs`에 기억. 여러 캐릭터를 한 번에 동기화할 때용
- 외부 경로(Downloads) 감시는 팀원 PC마다 경로가 달라 깨지므로 채택하지 않고, 프로젝트 안 폴더를 드롭 지점으로 삼음
- 검증: `귀족옷`을 툴 형식 그대로 드롭해 두었음(수동 rename 없이) → Unity 임포트 시 자동 정규화되는지로 확인

## 2026-09-16 — 귀족옷 자동 임포트 확인 + 캐릭터 디자인 관련 파일을 `Assets/조정식/`으로 이동

- `귀족옷` 드롭 자동화 검증 완료: `parts_*_sheets` → `idle/slash/walk` 자동 정리, `characters.txt` 5개 갱신 (Editor.log 확인)
- 캐릭터 디자인 관련 파일을 팀원 작업 폴더로 이동 (.meta 함께 이동해 GUID 유지):
  - `_Project/Resources/CharacterAnim/` → `조정식/Resources/CharacterAnim/` (`Resources` 폴더는 Assets 어디에 있어도 `Resources.Load` 동작)
  - `_Project/Scripts/CharacterAnim/` → `조정식/Scripts/CharacterAnim/`
  - `_Project/Scripts/Editor/CharacterAnimImporter.cs` → `조정식/Scripts/Editor/` (빈 `_Project/Scripts/Editor` 폴더는 삭제)
  - `_Project/Scripts/UI/{DesignSceneBootstrapper, CharacterPortraitUI}.cs` → `조정식/Scripts/UI/`
  - `Scenes/DesignScene.unity` → `조정식/DesignScene.unity` (부트스트랩은 씬 이름으로 판별하므로 영향 없음, 빌드 세팅엔 MainScene만 등록)
  - `Mercenary`/`MercenaryMarketGenerator`/`MercenaryMarketUI`/`PartyUI`는 게임 코어라 `_Project`에 유지 (같은 Assembly-CSharp이라 위치 무관)
- `CharacterAnimImporter.RootFolder`를 `Assets/조정식/Resources/CharacterAnim`으로 변경. **이후 새 캐릭터 드롭 위치도 이 폴더**
- 메뉴 "저장 폴더에서 캐릭터 가져오기" 보강: 상위 폴더 대신 캐릭터 폴더 자체(`parts_*_sheets`를 직접 가진 폴더)를 골라도 그 하나를 가져옴 — 사용자가 `귀족옷` 폴더를 직접 골라 `0개 가져옴`이 찍혔던 문제 대응

## 2026-09-16 — (버그 수정) Play 재시작 후 새로 가져온 캐릭터가 시장에 안 나오던 문제

- 원인: `ProjectSettings/EditorSettings.asset`의 Enter Play Mode Options에서 **Domain Reload가 꺼져 있음**(`m_EnterPlayModeOptions: 1`). Play를 껐다 켜도 static 필드가 초기화되지 않아 `CharacterAnimLibrary`의 캐릭터 목록/스프라이트 캐시가 첫 Play 값(5개)으로 남아 있었음. 머풀러 자체는 정상 임포트되어 있었음
- 수정: `CharacterAnimLibrary.ResetCache()`에 `[RuntimeInitializeOnLoadMethod(SubsystemRegistration)]`을 붙여 Play 진입마다 캐시 초기화 (Domain Reload가 꺼져 있어도 호출되는 Unity 권장 패턴)
- (참고) 같은 설정 때문에 `PlayerParty.Instance`, `PanelActivator._group` 같은 기존 static 싱글턴도 Play 간에 유지됨 — 지금은 문제로 드러나지 않았지만, "Play 다시 켰는데 상태가 남아 있다" 싶으면 이 설정을 먼저 의심할 것

## 2026-09-16 — (데이터 패치) 머풀러 목이 잘려 보이던 문제

- 원인: 코드가 아니라 원화. head 파츠는 6캐릭터 공통(맨몸 머리, 아래끝 y=126)인데 머풀러의 idle `torso.png`만 프레임 0·3에서 y=128부터 시작해 126~127 두 줄이 투명 → 턱 아래에 배경색 줄이 보임. walk/slash/slash2에는 빈 줄 없음
- 조치: `Assets/조정식/Resources/CharacterAnim/머풀러/idle/torso.png`를 스크립트로 패치 — 프레임마다 head bbox 아래끝과 torso bbox 위끝 사이가 비어 있으면 torso 첫 줄(머플러 윗줄)의 불투명 픽셀을 그 빈 줄에 복사해 채움. .meta는 그대로라 Unity는 재임포트만 함
- **주의**: 원본 `Downloads\저장\저장\머풀러`는 손대지 않았으므로 메뉴로 다시 가져오면 되돌아감. 근본 해결은 툴/Aseprite에서 머풀러 torso 목 부분(y 126~127)을 채워 다시 저장하는 것

## 2026-09-16 — (툴 쪽) pixel-anim-tool에 "그림 한 장으로 파츠 나누기" 추가

Unity 저장소 밖 작업이지만 파이프라인의 앞단이라 기록. 상세는 `Downloads\정리\README.md` §68.

- `C:\Users\wjdtl\pixel-anim-tool\index_213_slash_baked.html`에 버튼 `📷 그림 한 장으로 파츠 나누기` 추가. 옷 입힌 평면 그림 한 장을 **이 프로젝트의 `Assets/조정식/Resources/CharacterAnim/*/idle` 프레임 0에서 뽑은 파츠별 평균(vis/full 확률 맵)** 으로 torso/arm_f/arm_b/leg_f/leg_b/hair로 나눠 기존 자동 전파(검격/걷기/대기)로 넘김. 얼굴은 내장 head 사용
- 평균 데이터는 `정리\scripts\build_part_priors.py`가 Unity 폴더를 읽어 HTML에 주입 → **캐릭터가 늘면 다시 돌려야 정확도가 올라감** (현재 6개 기준, leave-one-out 라벨 정확도 0.91)
- 전제: 그림은 기존 캐릭터와 같은 rest 포즈·위치의 128×128

## 2026-09-16 — (툴 쪽) "파츠 나누기"를 독립 탭으로

- pixel-anim-tool 탭 바(애니메이션/내보내기/픽셀화)에 **파츠 나누기** 탭 추가. 그림 고르기 → 스테이지에서 파츠별 색 오버레이로 분할 결과 확인 → "파츠에 적용하고 딸깍"으로 검격/걷기/대기 생성. 애니메이션 탭 안에 있던 버튼은 제거. 실제 브라우저에서 업로드→적용까지 확인. 상세 `정리\README.md` §69

## 2026-09-16 — 파츠 나누기 "머리" 통합에 맞춰 head/hair를 한 세트로 조합

툴의 파츠 나누기가 이제 얼굴+머리카락을 **head 하나**로 내고 hair는 투명으로 비운다(`정리\README.md` §70). 그런 캐릭터를 head/hair 독립 랜덤으로 섞으면 머리카락이 겹치거나(남의 hair가 위에) 대머리(빈 hair가 남의 head에)가 되므로:

- `CharacterPartId`: `IndependentParts = {Head, Hair, Weapon}` → `HeadSetParts = {Head, Hair}` + `IndependentParts = {Weapon}`
- `RandomCharacterComposer.Compose`: 몸 세트 캐릭터 1개 + **머리 세트 캐릭터 1개**(head/hair 같은 폴더) + weapon 독립. 기존 캐릭터 6개는 head가 전부 같은 맨몸 얼굴이라 결과 차이 없음
- `DesignSceneBootstrapper` 정보 텍스트: "머리(얼굴+머리카락): X"
- Roslyn 컴파일 에러 0

## 2026-09-16 — (툴 쪽) 파츠 나누기 v3: 크기 자동 맞춤 + 옷만 적용

- 새 그림을 고르면 기존 캐릭터 평균 실루엣(높이 58.5·바닥 97·중심 65.3)에 맞춰 자동 축소·배치(윤곽선 보존 축소 규칙, 새 색 0). 머리/머리카락은 그림에서 버리고 **base 맨몸 머리**로 대체, 옷 5파츠만 적용. 상세 `정리\README.md` §72
- Unity 쪽 변경 없음. 이렇게 만든 캐릭터는 head = 맨몸 얼굴, hair = 투명이라 `RandomCharacterComposer`의 head/hair 세트 규칙(2026-09-16)으로 그대로 조합 가능(다른 캐릭터의 머리 세트가 붙으면 머리카락이 생김)

## 2026-09-16 — (툴 쪽) 파츠 나누기 메움 범위 버그 수정

- 바지/부츠 바깥에 살구색 테두리가 생기던 문제: "가려진 부분 메움"이 그림 실루엣 밖까지 채우던 것 → 실루엣 안에서 다른 파츠에 가려진 자리만 채우도록 한정. `정리\README.md` §73

## 2026-09-16 — (툴 쪽) "머리카락" 탭 추가

- 파츠 나누기와 같은 흐름으로 그림에서 머리카락만 뽑아 hair 슬롯에 적용하는 탭. 옷(파츠 나누기)과 머리카락을 서로 다른 그림에서 조합 가능. 살색 제외 + 머리카락 팔레트 색 필터로 머플러 같은 오염 제거. `정리\README.md` §74. Unity 변경 없음

## 2026-09-16 — (툴 쪽) 머리카락 탭을 얼굴 크기 기준으로

- 머리카락은 전신 높이가 아니라 그림 속 얼굴 폭을 base 얼굴 폭에 맞춰 축소·정렬 → base 머리를 제대로 덮는 크기가 됨(머리만 있는 그림도 동작). `정리\README.md` §75. Unity 변경 없음

## 2026-09-16 — (툴 쪽) 머리카락 폭 상한

- 머리카락 탭: 얼굴 기준 맞춤 뒤 머리카락 폭이 기존 캐릭터 평균(25px)을 넘으면 그만큼 더 축소 → 기존 캐릭터와 같은 크기(실측 폭 25, 위 y 37). `정리\README.md` §76

## 2026-09-16 — (툴 쪽) 머리카락 탭: 머리카락만 그린 그림 지원

- 얼굴이 없는 그림은 전체를 머리카락으로 보고 기존 캐릭터 머리카락 평균 크기(폭 25)·위치(위 y 38.5)에 맞춰 배치. 64/128 캔버스 무관. `정리\README.md` §77

## 2026-09-16 — 세계관 설정 (설계 논의, 코드 변경 없음)

향후 퀘스트/지역 시스템에 반영할 세계관을 정리. 아직 구현 착수 전, 설정만 확정.

- **지리**: 대륙 하나가 동서남북으로 구분. 중앙에 총용병협회 본부, 동서남북 각각에 지부 존재
  - 북 — 추운 설산 지대
  - 남 — 더운 항구도시
  - 동 — 사막 지대
  - 서 — 선선한 숲 지대
- **지역별 몬스터 3계층** (①인간형 약탈 세력 / ②지형 상징 동물 / ③지역 보스급)
  - 북(설산): 눈보라 도적단·빙하 부족 전사 / 얼음늑대(빙랑) / 설인(예티)
  - 남(항구): 해적단 / 대형 갑각류·전기가오리 / 크라켄
  - 동(사막): 사막 도적단·자칼전사 / 사막전갈 / 모래벌레(샌드웜)
  - 서(숲): 숲의 도적단·엘프 은둔자 / 다이어울프 / 트렌트(고목정령)
  - 중앙(총협회)은 컨셉 미정 — 나중에 별도로 정하기로 함
- **플레이어 진행**: 서쪽 지부에서 시작, 초반엔 서쪽 퀘스트만 진행. 레벨이 일정 수준 이상이 되면 북/남/동 지부로 진출 가능 (진입 조건 = 레벨 기준으로 확정)
- 아직 미정: 각 지역별 구체적 레벨 임계값, 북/남/동 진출 순서가 고정인지 자유 선택인지
- 참고: 퀘스트를 몇 시간~며칠 단위 게임 시간으로 스케일하고 서사형으로 바꾸는 별도 개편 계획과 맞물릴 예정 (예: "서쪽 숲의 다이어울프 무리 토벌" 같은 지역-몬스터 연계 퀘스트)

## 2026-09-16 — 세계관을 퀘스트/전투 시스템에 반영 (지역별 몬스터 3계층 + 레벨 진입 제한)

바로 위 세계관 설정을 코드로 연결. 서사형 텍스트·시간 스케일 개편은 아직 착수하지 않음(그 부분은 계속 보류) — 이번엔 "지역 + 3계층 몬스터 + 레벨 게이트"만 반영.

- `Scripts/World/Region.cs` 신설 — `enum Region { West, North, South, East }` (중앙은 컨셉 미정이라 제외)
- `Scripts/World/RegionInfo.cs` 신설 — 지역별 표시 이름·진입 레벨·3계층(약탈 세력/지형 동물/보스) 몬스터 이름 테이블. `GetMonsterName(region, tier, rng)`로 계층별 랜덤 이름 조회, `IsUnlocked(region)`/`GetUnlockedRegions()`로 진입 가능 지역 판단
  - **레벨 게이트 정의**: 서쪽은 항상 열림. 북/남/동은 `PlayerParty.Instance.Members`의 **최고 레벨 용병 기준** 레벨이 진입 레벨(현재 3개 지역 모두 10, 플레이스홀더) 이상이어야 열림 — 게임에 별도 "플레이어 레벨" 개념이 없어서 파티 최고 레벨을 대리 지표로 사용. 정확한 임계값과 이 정의 자체가 맞는지는 사용자 확인 필요
- `Quest`에 `Region` 필드 추가. `QuestGenerator.Generate(region, ...)`가 롤한 난이도(1/2/3)를 그대로 계층(약탈/동물/보스)에 매핑해 그 지역 몬스터 이름으로 제목 생성(`[서쪽] 다이어울프 3마리 처치` 형태). **보스 계층(난이도 3)은 항상 1마리만 등장**하도록 처치 수를 고정(그 외 계층은 기존처럼 Min~Max 랜덤)
- `QuestBoard.Refresh()`가 매 새로고침마다 `RegionInfo.GetUnlockedRegions()`에서 지역을 랜덤으로 골라 퀘스트를 생성 → 아직 레벨이 낮으면 퀘스트 게시판에 서쪽 퀘스트만 뜨고, 파티 최고 레벨이 10을 넘으면 북/남/동 퀘스트도 섞여서 등장
- `EnemySquadGenerator.Generate(region, count, difficulty, rng)`로 시그니처 변경 — 기존 고정 8종 몬스터 풀 제거하고 퀘스트와 같은 지역/계층 매핑으로 적 스쿼드 이름을 뽑음. 스탯 공식(공격/방어/체력/속도)은 그대로 유지, 이름만 지역화. 호출부 `PartyUI.StartBattle`에서 `quest.Region` 전달하도록 수정
- 검증: UnityMCP로 `refresh_unity(compile: request)` 후 콘솔 에러/경고 0건 확인. 실제 플레이(파티 없음→서쪽만 뜨는지, 레벨 10 이상 용병 보유 시 다른 지역도 섞이는지)는 에디터에서 확인 필요
- **후속 확인 필요**: (1) 지역 진입 레벨 임계값(현재 10, 모두 동일) 적절한지, (2) "플레이어 레벨 = 파티 최고 레벨 용병" 정의가 의도와 맞는지 — 아니라면 별도 플레이어/길드 레벨 개념을 새로 만들어야 함, (3) 북/남/동 진출이 자유 선택인지 순서가 있는지(현재는 레벨만 넘으면 3곳 다 동시에 열림)

## 2026-09-16 — 퀘스트 장소화(서쪽 초안) + 일 단위 소요시간 + 하루 지나기 + 동시 다중 파견

퀘스트를 "누구 몇 마리 처치"에서 "장소 + 목표"로, 소요시간을 초 단위 연출값이 아니라 며칠짜리 게임 시간으로, 그리고 서로 다른 용병 조합으로 여러 퀘스트를 동시에 진행할 수 있도록 확장. 요청 중 확인한 것: 임무 진행 중에도 다른 파티 편성으로 다른 퀘스트를 새로 보낼 수 있어야 함(예: a,b,c는 임무A로, d,f는 임무B로) — 즉 "파티 하나"가 아니라 "퀘스트별 파견대" 개념이 필요했음.

- **서쪽 지역 장소명 초안** (`RegionInfo`에 계층별로 추가, 나중에 요청 시 다른 지역도 추가 예정)
  - 약탈 세력: 초승달 숲(숲의 도적단) / 은빛 나무 마을(엘프 은둔자)
  - 지형 동물: 안개 협곡(다이어울프)
  - 보스: 천년 거목(트렌트)
  - 북/남/동은 아직 장소명 없음 — `QuestGenerator`가 장소명이 없으면 기존처럼 "{지역} {몬스터} N마리 처치" 형식으로 자동 폴백(크래시 없이 점진적으로 지역 추가 가능)
- `Quest`에 `LocationName`, `DurationDays`(생성 시 확정되는 총 소요일), `RemainingDays`(감소하는 남은 일수), `IsReady`, `AdvanceDay()` 추가. 기존 `EstimatedDurationSeconds`(전투 로그 연출 재생 속도용)는 **의도적으로 그대로 유지** — 게임 시간(일)과 화면 연출 시간(초)을 분리해야 한다는 기존 메모(퀘스트/시간 로드맵) 반영
  - 소요일수는 계층별로 랜덤: 약탈 세력 1~2일, 지형 동물 2~3일, 보스 3~5일 (구체적 숫자 미지정이라 임시로 정함)
  - 장소명이 있으면 제목이 `[서쪽] 초승달 숲 — 숲의 도적단 토벌` 형태로 생성됨
- `Scripts/World/GameClock.cs` 신설 — 전역 날짜 카운터(`CurrentDay`, 1부터 시작) + `AdvanceDay()` + `OnDayAdvanced` 이벤트. Domain Reload가 꺼져 있어도 Play 진입마다 1일차로 리셋되도록 `RuntimeInitializeOnLoadMethod(SubsystemRegistration)` 적용(2026-09-16 앞서 겪은 캐시 안 지워지는 버그와 같은 클래스라 처음부터 방지)
- `MainMenuBootstrapper`에 화면 **우상단 "N일차" 텍스트 + "하루 지나기" 버튼** 추가. 누르면 `GameClock.AdvanceDay()` 호출 → 이벤트를 구독하는 진행 중인 모든 파견의 남은 일수가 함께 줄어듦
- **다중 파견 시스템** (`Scripts/Quests/Expedition.cs`, `ExpeditionLog.cs` 신설)
  - `Expedition` = 퀘스트 1개 + 그 퀘스트에 배정된 용병 목록(스냅샷). `IsReady`는 `Quest.RemainingDays <= 0`
  - `ExpeditionLog` 싱글턴이 진행 중인 모든 파견대를 보관. 생성자에서 `GameClock.OnDayAdvanced`를 구독해 하루가 지날 때마다 모든 파견의 `Quest.AdvanceDay()`를 함께 호출 → 여러 퀘스트가 동시에 진행되고 각자 다른 날짜에 도착 완료됨. 같은 용병이 두 파견에 동시에 배정되는 것은 UI 단에서 막음(아래)
  - GameClock과 마찬가지로 Play 세션 리셋 적용
- **`PlayerParty` 단순화**: "전투 참가/제외(active)" 개념을 완전히 제거(`IsActive`/`SetActive`/`ToCombatants()` 삭제) — 이제 전투 팀은 파티 전체가 아니라 퀘스트별 `Expedition.Members`이므로 글로벌 활성 명단 개념 자체가 안 맞음. `Members`/`TryAdd`/`Remove`/`OnChanged`만 남음(순수 고용 명단 역할)
- **`PartyUI` 전면 재작성**:
  - "진행 중인 파견" 섹션 — 파견별로 대상/남은 일수(또는 "도착 완료")/배정된 용병 이름을 보여주고, 도착 완료된 파견만 개별 "전투 시작" 버튼이 활성화됨(각자 독립적으로 전투 실행·결과 재생)
  - 퀘스트보드에서 "수락"을 누르면(`QuestBoardUI` → `PartyUI.BeginDispatch`) "파견 편성" 모드로 전환 — 아직 다른 파견에 가 있지 않은(대기 중인) 용병만 체크(선택/선택됨 토글) 가능, 파견 중인 용병은 회색으로 표시되고 어떤 퀘스트에 가 있는지 문구로 표시되며 선택·해고 불가
  - 1명 이상 선택하면 "파견 시작" 버튼(패널 우하단, 기존 "전투 시작" 자리)이 활성화 → 누르면 `ExpeditionLog.Dispatch`로 새 파견 생성, 선택된 용병들은 그 즉시 다른 화면에서 "파견 중"으로 표시됨
  - 한 번에 여러 파견의 전투가 동시에 재생되는 것은 막음(`_battlingExpedition`으로 직렬화) — 결과 텍스트 영역이 하나뿐이라 동시 재생은 혼란스러움. 필요하면 나중에 파견별 결과창으로 분리 가능
- 검증: UnityMCP `refresh_unity(compile: request)` 두 차례 실행, 콘솔 에러/경고 0건(무관한 MCP 웹소켓 경고 1건 제외). 실제 플레이(파견 편성 UI, 하루 지나기로 남은 일수 감소, 여러 파견 동시 진행)는 에디터에서 확인 필요
- **디자인 판단(사용자 확인 필요)**: (1) 계층별 소요일수(1~2/2~3/3~5일) 구체 값, (2) 파견 인원수 제한 없음(현재 1명이든 전원이든 자유 편성) — 의도한 것인지, (3) 파견 중 실패 시(전멸 등) 페널티나 재시도 흐름은 아직 없음(기존 전투 로직 그대로 승패만 표시)

## 2026-09-17 — (버그 수정) "파견 시작" 버튼이 하루 지나기 UI에 가려 클릭 안 되던 문제

- 원인: `MainMenuBootstrapper`가 만든 "N일차/하루 지나기" 바를 Canvas 우상단(-16,-16)에 배치했는데, MarketPanel/PartyPanel/QuestPanel이 전부 **화면 우측에 붙어 있고**(우상단 anchor, 모서리에서 불과 20px 안쪽) `PartyUI`의 "파견 시작" 버튼도 패널 우상단 쪽(-44,-51)에 있어서, 두 UI가 사실상 같은 화면 영역을 차지함. 날짜 바가 패널보다 나중에(하이어라키 뒤에) 생성돼 위에 그려지고, `Text`는 기본적으로 `raycastTarget = true`라서 아래 버튼 클릭을 가로챔
- Unity RectTransform 좌표로 실제 겹침 확인(UnityMCP로 PartyPanel/Canvas RectTransform 조회): PartyPanel offsetMax `(-20,-20)`, 파견 시작 버튼이 그 안쪽 `(-44,-51)` — 날짜 바의 점유 영역과 겹침
- 수정: 날짜 바를 우상단 → **좌상단, 기존 메뉴 버튼바(용병시장/파티 구성/퀘스트) 바로 아래**로 이동(`anchoredPosition (16, -64)`). 이 열(x: 16~234)은 모든 패널이 우측에 붙어 있어 어떤 패널이 열려도 절대 겹치지 않는 확인된 여유 공간 — 향후 다른 전역 HUD 요소를 추가할 때도 이 자리를 재사용하면 안전
- 검증: 컴파일 에러 0. 실제 클릭 가능 여부는 에디터에서 확인 필요

## 2026-09-17 — (버그 재수정) 위 수정으로도 "하루 지나기"/"파티 구성" 버튼이 계속 겹치던 진짜 원인

바로 위 수정(좌상단으로 위치만 이동)으로도 사용자가 여전히 겹친다고 확인해줘서, UnityMCP로 **Play 모드에 직접 들어가** 두 버튼의 실제 RectTransform을 조회해 진짜 원인을 찾음.

- **진짜 원인**: `MenuButtonBar`/`DayControlBar` 둘 다 `HorizontalLayoutGroup.childControlWidth/Height = false`로 만들어져 있었음. 이 설정이면 레이아웃 그룹이 `LayoutElement.minWidth/minHeight`(버튼마다 120×40, 날짜 텍스트 90×40)를 **실제 RectTransform 크기에 전혀 반영하지 않고** 각 자식의 크기가 Unity 기본값인 **100×100**으로 남아 있었음(Play 모드 실측: `퀘스트Button`/`AdvanceDayButton` 모두 `rect: {width:100, height:100}`). 즉 버튼이 의도한 것보다 세로로 2.5배 큰 보이지 않는 영역까지 클릭을 가로채고 있었고, 그래서 두 바를 8px 간격으로 떨어뜨려 배치해도(의도한 40px 높이 기준 계산) 실제로는 100px짜리 히트박스끼리 52px나 겹쳤던 것
  - 이 버그는 이번에 새로 만든 게 아니라 **기존 `CreateMenuButton`/`CreateMenuButton` 패턴에 원래 있던 것**(처음부터 `childControlWidth/Height = false`였음) — 지금까지는 MenuButtonBar 근처에 아무것도 없어서 안 드러났을 뿐, 이번에 바로 아래에 DayControlBar를 추가하면서 처음 노출된 것
- 수정: `MainMenuBootstrapper.CreateMenuBar`/`CreateDayControls` 두 곳 모두 `childControlWidth = true`, `childControlHeight = true`로 변경 — 이제 레이아웃 그룹이 `LayoutElement`값대로 자식 크기를 실제로 적용함(버튼이 진짜 120×40, 날짜 텍스트가 진짜 90×40이 됨)
- 재검증(Play 모드 실측): `파티 구성Button` y범위 1024~1064, `AdvanceDayButton` y범위 946~986 — 겹침 없음(38px 여유). 컴파일 에러 0
- **교훈**: 이 프로젝트에서 `HorizontalLayoutGroup`을 `childControlWidth/Height = false` + `LayoutElement.minWidth/minHeight`로 쓰는 패턴은 **크기가 적용 안 되고 항상 100×100으로 남는 버그**가 있음. 앞으로 새 UI를 이 패턴으로 만들 때는 반드시 `childControlWidth/Height = true`로 할 것(이미 존재하는 다른 곳에도 같은 패턴이 남아있을 수 있어 UI 클릭 이슈가 또 생기면 먼저 의심할 것)

## 2026-09-17 — 스페이스바로도 하루 지나기

- `Scripts/UI/DayAdvanceInput.cs` 신설 — `Update()`에서 `Input.GetKeyDown(KeyCode.Space)`면 `GameClock.AdvanceDay()` 호출. `DayControlBar`(우상단→좌상단으로 옮긴 그 날짜 바) GameObject에 컴포넌트로 붙임(`MainMenuBootstrapper.CreateDayControls`에서 생성 시 추가)
- 겸사겸사 날짜 텍스트 갱신 로직 정리: 버튼 클릭 핸들러 안에서 직접 `dayText.text`를 갱신하던 걸 빼고, `GameClock.OnDayAdvanced` 이벤트에 구독시켜 갱신하도록 통일 → "하루 지나기" 버튼 클릭이든 스페이스바든 경로 상관없이 텍스트가 항상 같이 갱신됨. 버튼 onClick도 `GameClock.AdvanceDay`를 직접 리스너로 연결(람다 불필요)
- 검증: 컴파일 에러 0. 스페이스바 실제 입력은 자동화 테스트 도구가 없어 코드 검토로만 확인(`Input.GetKeyDown`은 표준 API라 동작 확신, 에디터에서 직접 눌러 확인 권장)

## 2026-09-17 — (버그 수정) 스페이스바가 안 먹던 원인: 레거시 Input 클래스가 이 프로젝트에서 막혀 있었음

- 사용자가 Play 모드에서 스페이스바를 눌러도 날짜가 안 넘어가고 콘솔에 에러가 있다고 확인해줌. 콘솔 확인 결과 매 프레임 `InvalidOperationException: You are trying to read Input using the UnityEngine.Input class, but you have switched active Input handling to Input System package in Player Settings.` (`DayAdvanceInput.cs:11`, `Input.GetKeyDown` 호출부)
- 원인: 이 프로젝트는 Player Settings의 Active Input Handling이 "Input System Package" 단독으로 설정돼 있어서(레거시 `UnityEngine.Input` 비활성화) — `DesignSceneBootstrapper`가 `InputSystemUIInputModule`을 쓰는 것과 같은 맥락. 새로 만든 `DayAdvanceInput`만 옛날 `UnityEngine.Input.GetKeyDown`을 썼던 게 문제
- 수정: `Input.GetKeyDown(KeyCode.Space)` → `Keyboard.current.spaceKey.wasPressedThisFrame`(`UnityEngine.InputSystem`)로 교체. `Keyboard.current`가 null일 수 있어 null 체크 추가
- 검증: 컴파일 에러 0, 콘솔 클리어 후 에러 재발 없음. 실제 스페이스바 입력 동작은 에디터에서 직접 확인 필요

## 2026-09-17 — 이동 중 랜덤 습격 이벤트 (체력 소모 + 사망 가능)

"용병들이 임무를 출발하고 도착지에 도착할 동안 일어날 수 있는 랜덤이벤트"(도적단 습격 등, 체력 소모·사망 가능) 요청. 기존 전투/지역 시스템(AutoBattleSimulator, EnemySquadGenerator, RegionInfo의 "약탈 세력" 계층)을 그대로 재사용해 구현 — 새로운 데미지 공식이나 몬스터 목록을 따로 만들지 않음.

- **용병에게 처음으로 "영구 체력" 개념 도입** (`Mercenary.cs`): `CurrentHealth`(생성 시 `CurrentStats.MaxHealth`로 초기화) + `IsAlive` + `SetHealth(value)`(0~MaxHealth 클램프). 기존에는 전투(`Combatant`)가 끝나도 용병 본체엔 아무 흔적이 안 남았는데, 이제 `Mercenary.ToCombatant()`가 `CurrentHealth`만큼 미리 깎인 `Combatant`를 만들어서(생성 직후 `TakeDamage(maxHealth - currentHealth)`) 이전에 입은 피해가 다음 전투에도 그대로 이어짐 — 도착 전투를 이미 다친 상태로 시작할 수 있음
- **습격 판정** (`ExpeditionLog.HandleDayAdvanced` 확장): 하루가 지날 때, **아직 이동 중이던**(그날 하루를 여행으로 쓴) 각 파견대에 대해 25% 확률로 습격 발생
  - 발생 시 `EnemySquadGenerator.Generate(quest.Region, 1~3마리, difficulty: 1, rng)`로 그 지역의 **약탈 세력 계층(도적단 등)** 몬스터를 생성 — 지형 동물/보스가 아니라 사용자가 예로 든 "도적단/산적" 톤에 맞춰 항상 계층 0(난이도 1) 고정
  - 살아있는 파견 인원만 전투원으로 만들어 `AutoBattleSimulator`로 짧게(최대 20라운드) 자동 교전 → 결과(`Combatant.CurrentHealth`)를 그대로 각 `Mercenary.SetHealth()`에 반영
  - 체력이 0이 된 용병은 **그 자리에서 영구 사망** — `PlayerParty.Instance.Remove()`로 고용 명단에서도 완전히 빠짐(해고와 동일한 처리 경로)
  - 파견 인원 전원이 죽으면 "파견대가 전멸했다..." 메시지와 함께 `ExpeditionLog.Complete()`로 그 파견 자체를 즉시 종료(목적지 전투 없이 끝남)
  - `ExpeditionLog.OnTravelEvent(string)` 이벤트로 결과 문구를 알림(예: `[서쪽] 초승달 숲 — 숲의 도적단 토벌] 이동 중 숲의 도적단의 습격! 카이런 체력 12/28. 브렌 사망.`) → `PartyUI`가 구독해서 기존 결과 텍스트 영역에 표시
- **UI 반영**: `PartyUI`의 "보유 용병" 줄이 `체{MaxHealth}` → `체{CurrentHealth}/{MaxHealth}`로 바뀌어 다친 용병이 눈에 보임. "진행 중인 파견" 줄의 인원 표시도 사망자는 `이름(사망)`으로 표시. 도착 전투(`StartExpeditionBattle`)는 이제 `expedition.Members.Where(IsAlive)`만 참전시킴(죽은 용병은 목적지 전투에 안 나감)
- 검증: UnityMCP `refresh_unity(compile: request)` 두 차례(첫 시도는 `System.Random` vs `UnityEngine.Random` 네임스페이스 충돌로 컴파일 에러 1건 → `System.Random`으로 명시해 수정), 최종 에러 0. 실제 플레이(습격 발생 빈도, 사망/생존 흐름)는 에디터에서 확인 필요
- **디자인 판단(사용자 확인 필요)**: (1) 습격 확률 25%, 습격 인원 1~3명 — 감 잡아서 임시로 정함, (2) 습격 몬스터는 항상 그 지역 "약탈 세력" 계층 고정(지형 동물/보스는 이동 중엔 안 나옴 — 목적지 도착 전투에서만 등장), (3) **회복 수단이 아직 없음** — 한 번 깎인 체력은 시간이 지나도 저절로 안 돌아오고, 다치면 계속 다친 채로 남아 다음 전투를 그 상태로 시작함(휴식/치료 시스템은 다음 후속 작업으로 보임), (4) 여러 파견이 같은 날 동시에 습격당하면 결과 텍스트 창이 하나뿐이라 마지막 메시지만 남음(기존에 이미 알려진 한계와 동일)

## 2026-09-17 — 휴식 버튼 (하루 더 걸리는 대신 체력 완전 회복)

바로 위에서 언급한 "회복 수단 없음" 문제 해결. "휴식을 하면 하루가 더 걸리고 체력회복이 되는식으로" 요청대로 구현.

- `Quest.Delay(int days)` 추가 — 습격 등으로 도착이 늦어질 때 `RemainingDays`를 늘리는 용도. 기존 `AdvanceDay()`(줄이는 쪽)와 대칭
- `Mercenary.HealFully()` 추가 — `CurrentHealth`를 `CurrentStats.MaxHealth`로 완전 회복(부분 회복이 아니라 풀 회복으로 정함 — 하루를 통째로 쓰는 대가가 있으니 화끈하게)
- `Expedition.Rest()` — 생존 인원 전원 `HealFully()` + `Quest.Delay(1)`. `ExpeditionLog.Rest(expedition)`가 이 호출을 감싸고 `OnTravelEvent`로 "휴식을 취해 체력을 회복했다. (남은 일수 +1일)" 문구를 알린 뒤 `OnChanged` 발행 → UI 갱신
- `PartyUI`의 "진행 중인 파견" 행에 "휴식(+1일)" 버튼 추가(전투 시작 버튼 옆). **다친 인원이 한 명도 없으면 버튼이 비활성화**되어 의미 없이 하루를 낭비하는 실수를 막음. 도착 완료(`IsReady`) 상태에서도 휴식 가능하게 열어둠 — 목적지 전투 직전에 미리 체력을 채워두는 용도로도 쓸 수 있음(휴식하면 RemainingDays가 다시 1이 되어 "이동 중"으로 돌아가고, 다시 하루 지나기를 누르거나 또 휴식하면 됨)
- 검증: 컴파일 에러 0. 실제 플레이(휴식 버튼 클릭 → 남은 일수 +1, 체력 풀회복 확인)는 에디터에서 확인 필요
- **디자인 판단(사용자 확인 필요)**: 완전 회복(100%)으로 정함 — 부분 회복이나 "야영 실패 확률"(휴식 중에도 습격당할 수 있음) 같은 리스크는 아직 없음. 필요하면 다음에 조정 가능

## 2026-09-17 — 퀘스트 난이도 조정: 파견 인원 상한 도입

사용자 피드백 "퀘스트 난이도가 너무 쉬워". 수치 분석 결과, 적 스탯/숫자가 난이도에만 비례하고 **파견 인원수와는 무관**한 게 원인으로 확인됨 — 특히 보스 계층(난이도3)은 항상 1마리라, 파티 5명을 보내면 턴 순서상 라운드당 파티가 5번 때리는 동안 보스는 1번만 때려서 첫 라운드에 녹는 수준(평균 체력 44). 사용자에게 조정 방향(인원 상한/적 스탯 상향/적 숫자 비례/보스 전용 로직)을 물어봤고 **"파견 인원 상한 도입"**을 선택함.

- `Quest.MaxDispatchSize` 추가 — `EnemyCount + 1`. 보스(EnemyCount=1)는 최대 2명, 약탈 세력/지형 동물(EnemyCount 2~5)은 최대 3~6명까지만 파견 가능 — 항상 "적 숫자+1"로 묶여서 압도적 물량으로 밀어붙이는 게 원천적으로 안 됨
- `PartyUI` 파견 편성 화면: 섹션 헤더에 `(선택수/상한 명)` 표시, 상한에 도달하면 아직 안 뽑은 용병의 "선택" 버튼이 비활성화됨(눌러도 무시가 아니라 아예 안 눌리는 걸 보여줌). `ToggleSelection`에도 방어적으로 상한 체크 추가(버튼이 막혀 있어도 로직 자체가 상한을 넘기지 않음)
- 검증: 컴파일 에러 0. 실제 밸런스 체감(특히 보스전이 이제 위협적으로 느껴지는지)은 에디터에서 확인 필요
- **다음에 더 만질 수 있는 지점**(이번엔 반영 안 함, 사용자가 인원 상한만 선택): 적 개인 스탯 자체 상향, 적 숫자를 파견 인원에 비례시키는 것, 보스 전용 특수 로직(다회 공격/광역기 등) — 인원 상한만으로도 부족하면 다음 후보

## 2026-09-17 — 새 직업 "길잡이" 추가 (전투 패시브 없음, 습격 회피 전담)

"전투에 직접적으로 가담하는 스킬은 없지만, 파견 갈 때 발생하는 습격 이벤트를 확률적으로 피하게 해주는 직업" 요청.

- `MercenaryClassKind`에 `Guide` 추가 (기존 Warrior/Archer/Healer/Assassin 뒤에 4번째)
- `ClassPassiveFactory.Create`는 손 안 댐 — 두 switch문 모두 `Guide` 케이스가 없어서 기존 `default: return new List<PassiveBase>()`로 자연히 빠짐(일반/레어 패시브 롤 여부와 무관하게 항상 빈 패시브 목록) → "전투 스킬 없음" 요구를 코드 수정 없이 만족
- `Guide.asset` 신규 생성(UnityMCP `manage_scriptable_object`로 정식 에셋 파이프라인을 통해 생성 — GUID/메타 자동 처리). 전투력은 낮게, 기동력은 높게 잡음: 공격5(+1/lv) 방어3(+1/lv) 체력26(+4/lv) 이동속도10(+1/lv, 회피 확률에 영향). `Assets/_Project/Resources/MercenaryClasses/` 폴더 안이라 `MercenaryMarketUI`가 `Resources.LoadAll`로 자동 인식 — 코드 변경 없이 시장에 등장
- **습격 회피 메커니즘** (`ExpeditionLog.TryTriggerAmbush`): 습격이 발생 판정(25%)된 뒤, 파견 인원 중 살아있는 길잡이가 한 명이라도 있으면 50% 확률로 그 습격을 통째로 무효화(`GuideAvoidChance`). 성공하면 "길잡이 덕분에 습격을 피했다." 문구만 뜨고 전투/피해 없이 지나감. 길잡이가 여러 명이어도 회피 확률은 중첩 안 됨(있고 없고만 확인, 단순화)
- 검증: 컴파일 에러 0(enum 추가 후 1차 컴파일 확인 → 에셋 생성 → 2차 컴파일 확인). 실제 시장에 길잡이가 뜨는지, 습격 회피가 동작하는지는 에디터에서 확인 필요
- **디자인 판단(사용자 확인 필요)**: (1) 회피 확률 50% — 감으로 정함, (2) 길잡이 스탯(전투력 낮음/기동력 높음) — 순수 유틸 직업으로 설계했는데 그래도 전투에 데려갈 유인이 있는 수치인지는 플레이로 확인 필요, (3) 길잡이 여러 명 데려가도 회피 확률 안 오름(스택 없음) — 한 명만 있으면 충분하다는 전제

## 2026-09-17 — 습격 난이도를 퀘스트 난이도에 비례하도록 상향

사용자 피드백: "습격의 난이도가 너무 약해서 굳이 데려갈 필요가 안 생겨" (길잡이 얘기). 원인: 습격이 퀘스트 난이도와 무관하게 **항상 난이도1 고정 스탯 + 최대 3마리**로만 생성되고 있었음 — 보스급(난이도3) 원정을 가도 가는 길의 습격은 항상 똑같이 약했음.

- `EnemySquadGenerator.Generate`에 `tierOverride` 파라미터 추가 — 지정하면 "이름 계층"(약탈 세력/지형 동물/보스)은 그 값으로 고정하고, `difficulty`는 스탯 강도 계산에만 쓰임. 기존 호출부(퀘스트 본 전투)는 그대로 동작(파라미터 생략 시 기존처럼 difficulty로 계층까지 결정)
- `ExpeditionLog.TryTriggerAmbush` 수정: 습격 스탯 난이도를 고정값 1 → **`expedition.Quest.Difficulty` 그대로** 사용(이름은 `tierOverride: 0`으로 항상 "약탈 세력" 고정 — 습격이 갑자기 보스 몬스터 이름으로 뜨진 않음, 스탯만 그 난이도만큼 세짐). 습격 인원수 상한도 고정값 3 → **`min(생존 인원, 퀘스트 EnemyCount)`**로 변경 — 본전투 규모와 습격 규모가 같이 커짐
- 결과: 난이도1 퀘스트의 습격은 기존과 비슷하게 약한 채로 남지만, **난이도가 높은(특히 보스급) 퀘스트일수록 오가는 길의 습격도 같이 위협적으로 강해짐** — 보스 원정(EnemyCount=1, 파견 상한 2명)은 습격도 보스급 스탯의 "약탈 세력" 1마리와 마주치게 됨. 위험한 지역일수록 길잡이의 회피가 실제로 아쉬워지는 구조
- 검증: 컴파일 에러 0. 실제 체감 난이도(특히 고난도 퀘스트에서 습격이 위협적으로 느껴지는지)는 에디터에서 확인 필요
- **디자인 판단(사용자 확인 필요)**: 습격 발생 확률(25%) 자체는 안 건드림(요청이 "난이도"였지 "빈도"가 아니라고 판단) — 빈도도 같이 올리고 싶으면 말씀해주시면 됨

## 2026-09-17 — (버그 수정) 이전 임무 결과 텍스트가 다음 퀘스트 수락 후에도 남아있던 문제

- 원인: `PartyUI.resultText`는 전투 재생이 끝난 뒤(`PlayBattle`) 텍스트를 남겨둔 채로 그대로 방치됨. 새 퀘스트를 수락(`BeginDispatch`)해도 지워주는 코드가 없어서, 새로 "전투 시작"을 눌러 그 파견의 `PlayBattle`이 시작되기 전까지는 화면에 직전 임무 결과가 계속 떠 있었음
- 수정: `PartyUI.BeginDispatch(quest)`에서 `_pendingQuest` 설정 직후 `RenderResultText(string.Empty)` 호출 — 퀘스트를 수락하는 순간 결과 텍스트가 바로 비워짐
- 검증: 컴파일 에러 0. 에디터에서 직접 확인 필요
