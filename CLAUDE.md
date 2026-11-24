# 4_Asgard

## 에셋 제작 규칙
- 사용자가 만들어 달라고 하는 에셋(3D 모델, 이미지, 텍스처, 리깅, 애니메이션)은 모두 **VARCO 3D 커넥터**("바로크")로 생성한다. Unity AI 생성 기능은 쓰지 않는다 (Unity 계정에 구독 없음).
- 작업은 항상 VARCO의 **McpTest** 플로우(flowId `16045f17-06c2-403a-896c-43490bb7b178`)에서 한다. 새 플로우를 만들지 않는다.
- 흐름: VARCO 플로우 생성/실행 → `get_output_downloads`로 결과 받기 → `Assets/` 아래에 GLB/FBX 저장 → Unity MCP(`Unity_RunCommand`)로 씬에 배치.
- 포인트는 신경 쓰지 않고 필요한 만큼 생성해도 된다 (사용자 허락됨).
- 메시는 로우폴리로: 최대 2만 폴리곤, 가능하면 더 낮게 (리메시/폴리곤 수 옵션으로 조절).

## 연결
- Unity MCP: `.mcp.json`의 `unity-mcp` (relay_win.exe, `--project-path`로 이 프로젝트 지정)
