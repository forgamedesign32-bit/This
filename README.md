# This War of Mine — 적 AI 개선 프로토타입 (Unity 6 / URP)

This War of Mine 야간 수색 단계의 적 AI 개선안을 보여주는 프로토타입입니다.

- **F1 순찰·경계:** 순찰 경로와 대기 시간이 무작위이고, 경비마다 경계 수준에 따라 행동이 달라집니다.
- **F2 분대 협력:** 경비들이 침입자 정보를 공유하고, 추적조·우회조·출입구 경계조로 역할을 나눕니다.
- **F3 주민 반응:** 주민이 도주하고, 문을 잠그고, 숨거나, 살려 달라고 빕니다.

## 바로 실행 (Windows 빌드)
[`Release/TWOM_AI_Prototype_Win64.zip`](Release/TWOM_AI_Prototype_Win64.zip)을 내려받아 압축을 풀고 `Build/This_Proto.exe`를 실행합니다.

QA 단축키:
- F5: Legacy/Enhanced 모드 전환
- F6: 재시작
- F7: 무적
- F10: 유령 모드
- F11: 테스트 소음 (`[` `]` 키로 크기 조절)

## 프로젝트 열기
1. Unity 6000.x로 프로젝트를 엽니다. 버전은 `ProjectSettings/ProjectVersion.txt`를 확인하세요.
2. 메뉴 `Tools/TWOM AI/Build Prototype Scene`을 실행한 뒤 Play 합니다.

## 문서
- 변경 이력과 기능 설명: [`Assets/TwomAi/README.md`](Assets/TwomAi/README.md)
- 스크립트별 코드 구조: [`Assets/TwomAi/CODE_STRUCTURE.md`](Assets/TwomAi/CODE_STRUCTURE.md)

## 포함된 외부 에셋
- Starter Assets – First/Third Person (Unity): 캐릭터 모델 `Armature`, 애니메이터 컨트롤러
- Low Poly Weapons VOL.1 (AK74 등)
- Mixamo 애니메이션: `Assets/TwomAi/Animations`

이 에셋들은 각 원저작자의 라이선스를 따릅니다.
