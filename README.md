# This War of Mine — 적 AI 개선 프로토타입 (Unity 6 / URP)

This War of Mine 야간 수색 단계의 적 AI 개선안을 보여주는 프로토타입입니다.

- **F1 순찰·경계:** 순찰 경로와 대기 시간이 무작위이고, 경비마다 경계 수준에 따라 행동이 달라집니다.
- **F2 분대 협력:** 경비들이 침입자 정보를 공유하고, 추적조·우회조·출입구 경계조로 역할을 나눕니다.
- **F3 주민 반응:** 주민이 도주하고, 문을 잠그고, 숨거나, 살려 달라고 빕니다.

## 프로젝트 열기
1. Unity 6000.x로 프로젝트를 엽니다. 버전은 `ProjectSettings/ProjectVersion.txt`를 확인하세요.
2. **Starter Assets – ThirdPerson** 패키지를 Asset Store에서 임포트합니다. 이 패키지는 저장소에 포함되어 있지 않습니다.
   - 캐릭터 모델 `Armature`와 애니메이터 컨트롤러를 여기서 가져다 씁니다.
3. 메뉴 `Tools/TWOM AI/Build Prototype Scene`을 실행한 뒤 Play 합니다.

## 문서
- 변경 이력과 기능 설명: [`Assets/TwomAi/README.md`](Assets/TwomAi/README.md)
- 스크립트별 코드 구조: [`Assets/TwomAi/CODE_STRUCTURE.md`](Assets/TwomAi/CODE_STRUCTURE.md)

## 포함된 외부 에셋
- Low Poly Weapons VOL.1 (AK74 등)
- Mixamo 애니메이션: `Assets/TwomAi/Animations`

이 에셋들은 각 원저작자의 라이선스를 따릅니다.
