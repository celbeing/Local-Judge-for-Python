# Local Judge Handoff

작성일: 2026-05-29  
작업 위치: `C:\Users\kimsd\source\repos\celbeing\Class-Code`  
현재 작업 솔루션: `Local Judge\Local Judge.sln`
주의: `Class_code`는 만들다 중단한 이전 솔루션이고, 현재 기능 개발 대상은 `Local Judge`입니다.

## 현재 상태 요약

- `Local Judge`는 .NET 8 WPF 기반 로컬 저지 앱입니다.
- 코드 편집기는 WebView2 + Monaco Editor를 사용합니다.
- 현재 실행 언어는 Python 중심입니다.
- Python 실행, 예제 실행, 제출, 제출 이력 저장/조회/내보내기/가져오기 기능이 구현되어 있습니다.
- 프로그램 시작 시 채점 환경 벤치마크를 필수 실행하고, 결과를 로컬 적용 시간/메모리 제한에 반영합니다.
- 최근 큰 작업은 문항 편집기와 문항 표시 UI 개선입니다.
- 문항 본문은 HTML 저장이 아니라 `Markdown + LaTeX` 원문으로 JSON에 저장합니다.
- 문항 표시는 WebView2 기반 `ProblemViewer`로 렌더링합니다.
- 문항 설명용 이미지는 JSON에 base64로 넣지 않고, 문제 JSON 옆 `{문제파일명}.assets` 폴더에 저장합니다.

## 빌드 검증

기본 빌드 명령:

```powershell
dotnet build "Local Judge\Local Judge.sln" --no-restore
```

단, 실행 중인 `Local Judge.exe` 또는 Visual Studio가 `bin\Debug` 출력 파일을 잠그면 기본 빌드가 실패할 수 있습니다. 그 경우 임시 출력 경로로 컴파일 검증합니다.

```powershell
dotnet build "Local Judge\Local Judge.sln" --no-restore -p:OutputPath="$env:TEMP\localjudge-build\"
```

마지막 확인 기준으로 임시 출력 경로 빌드는 경고 0개, 오류 0개로 통과했습니다.

## 핵심 파일

- `Local Judge\MainWindow.xaml`
- `Local Judge\MainWindow.xaml.cs`
- `Local Judge\ProblemDocument.cs`
- `Local Judge\ProblemAssetUtilities.cs`
- `Local Judge\ProblemEditorWindow.xaml`
- `Local Judge\ProblemEditorWindow.xaml.cs`
- `Local Judge\ProblemViewer\index.html`
- `Local Judge\ProblemViewer\problemViewer.css`
- `Local Judge\ProblemViewer\problemViewerHost.js`
- `Local Judge\PythonRunner.cs`
- `Local Judge\JudgeEnvironmentBenchmark.cs`
- `Local Judge\SubmissionHistoryStore.cs`
- `Local Judge\SubmissionHistoryExporter.cs`
- `Local Judge\SubmissionHistoryImportReader.cs`
- `Local Judge\SubmissionHistoryWindow.xaml(.cs)`
- `Local Judge\SubmissionHistoryFileWindow.xaml(.cs)`

## 문항 스키마

`ProblemDocument.cs`가 문제 JSON 스키마입니다.

현재 주요 필드:

- `Version`
- `Id`
- `Title`
- `AuthorName`
- `Source`
- `TimeLimitMs`
- `MemoryLimitMb`
- `StatementFormat`
- `Description`
- `InputFormat`
- `OutputFormat`
- `Assets`
- `Samples`
- `TestCases`

새 문제는 다음 기준으로 저장합니다.

- `version: 4`
- `statementFormat: "markdown-latex"`

기존 `version: 3` JSON 문제는 계속 불러올 수 있습니다. 기존 파일에 `statementFormat`이 없으면 평문 문제로 취급합니다.

## 문항 편집기

주요 파일:

- `ProblemEditorWindow.xaml`
- `ProblemEditorWindow.xaml.cs`
- `ProblemAssetUtilities.cs`

현재 동작:

- 편집기는 좌우 분할 구조입니다.
- 왼쪽은 문제 정보, Markdown/LaTeX 본문 입력, 예제, 테스트 ZIP 등록 영역입니다.
- 오른쪽은 WebView2 실시간 미리보기입니다.
- 새 문제 생성 시 `저장` 버튼은 파일 저장 대화상자만 띄우고, 메인 윈도우에 자동 불러오지 않습니다.
- 기존 문제 수정 시 수정 결과를 메인 윈도우에 반영하고 JSON 저장 흐름을 탑니다.
- 문항 제작자와 출처는 새 문제 생성 시에만 입력 가능하며, 기존 문제 수정 시 읽기 전용입니다.
- 예제는 추가/삭제 가능합니다.
- 예제 1은 삭제할 수 없고, 예제 2부터 삭제 버튼이 표시됩니다.
- 채점 테스트는 ZIP으로 등록합니다.
- `.in` 파일과 같은 이름의 `.out` 파일을 매칭합니다.
- 매칭되지 않는 `.in` 또는 `.out`이 있으면 에러 메시지를 띄웁니다.

## 이미지 Asset

이미지 삽입은 지원됩니다.

지원 확장자:

- `png`
- `jpg`
- `jpeg`
- `gif`
- `webp`

저장 방식:

- JSON에 base64로 넣지 않습니다.
- 문제 JSON 옆에 `{문제파일명}.assets` 폴더를 만들고 이미지 파일을 저장합니다.
- Markdown 본문에는 다음 형태로 들어갑니다.

```markdown
![image-name](assets/image-name.png)
```

`ProblemAssetUtilities.cs`가 다음을 담당합니다.

- asset 폴더 경로 계산
- 이미지 파일명 충돌 방지
- content type 판별
- asset 폴더 복사
- asset 목록 clone

다른 이름으로 문제를 저장할 때 asset 폴더도 함께 복사하도록 되어 있습니다.

## 문항 표시 UI

메인 화면의 좌측 문제 표시 영역은 기존 TextBox 묶음에서 WebView2 문제 뷰어로 교체되었습니다.

주요 파일:

- `ProblemViewer\index.html`
- `ProblemViewer\problemViewer.css`
- `ProblemViewer\problemViewerHost.js`

렌더링 순서:

- 제목
- 메타 정보
- 문제 설명
- 입력
- 출력
- 모든 예제 입력/출력

예제는 첫 번째만 보여주지 않고 전체 sample을 렌더링합니다.

이미지는 WebView2 virtual host mapping을 통해 `assets/` 경로만 표시합니다. 외부 URL 이미지는 v1에서 지원하지 않는 방향입니다.

## Markdown 지원 범위

현재 `ProblemViewer\problemViewerHost.js`의 자체 렌더러가 처리합니다.

지원:

- 제목 `#`, `##`, `###`
- 문단과 줄바꿈
- 굵게 `**text**`
- 기울임 `*text*`
- 순서/비순서 목록
- 인라인 코드
- 코드 블록
- 표
- 링크 라벨 표시
- 이미지 `![alt](assets/file.png)`

링크는 외부 이동을 하지 않고 라벨만 표시합니다.

## LaTeX 지원 범위

현재는 KaTeX/MathJax가 아니라 간단한 자체 변환기입니다. 복잡한 수식 조판은 아직 제한적입니다.

지원:

```latex
$A+B$
```

```latex
$$
A_i \leq B_i
$$
```

지원 명령:

- `\leq`, `\le`
- `\geq`, `\ge`
- `\neq`, `\ne`
- `\times`, `\cdot`, `\pm`
- `\infty`, `\ldots`, `\dots`
- `\sum`, `\prod`
- `\alpha`, `\beta`, `\gamma`, `\delta`, `\epsilon`, `\theta`, `\lambda`, `\mu`, `\pi`, `\sigma`, `\phi`, `\omega`
- `\rightarrow`, `\to`

지원 구조:

- `\frac{a}{b}`
- `\sqrt{x}`
- `x^2`, `x^{n+1}`
- `a_i`, `a_{i+1}`

제한:

- 중첩 분수는 제한적입니다.
- `align`, `cases`, `matrix`, `\left`, `\right` 등은 아직 미지원입니다.
- 복잡한 수식이 필요하면 다음 단계에서 KaTeX 오프라인 번들을 붙이는 것이 좋습니다.

## Python 실행 및 채점

주요 파일:

- `PythonRunner.cs`
- `JudgeEnvironmentBenchmark.cs`
- `MainWindow.xaml.cs`

구현 상태:

- Python 실행 분리 완료
- 시간 제한 지원
- 출력 제한 지원
- 메모리 제한 지원
- 결과 분류: `AC`, `WA`, `TLE`, `MLE`, `RE`, `OLE`
- 예제 실행 터미널에서는 `PASS`, `FAIL`도 색상 강조
- 실행 중 중지 버튼 연결
- 벤치마크 중에는 예제 실행/제출이 비활성화됩니다.

## 채점 환경 벤치마크

`JudgeEnvironmentBenchmark.cs`가 담당합니다.

동작:

- 프로그램 시작 시 필수 실행
- 메뉴에서 수동 재실행 가능
- 벤치마크 중 상태 표시줄에 채점 환경 점검 상태 표시
- 결과로 시간 배율, 추가 시간, 추가 메모리를 계산
- 문항의 이상적 제한에 벤치마크 결과를 반영해 로컬 적용 제한 계산

## 제출 이력

주요 파일:

- `SubmissionHistoryStore.cs`
- `SubmissionHistoryExporter.cs`
- `SubmissionHistoryImportReader.cs`
- `SubmissionHistoryWindow.xaml`
- `SubmissionHistoryWindow.xaml.cs`
- `SubmissionHistoryFileWindow.xaml`
- `SubmissionHistoryFileWindow.xaml.cs`

구현 상태:

- 제출 시도 저장
- 제출 이력 조회 UI
- 제출 이력 ZIP 내보내기
- 제출 이력 파일 확인 UI
- 제출 언어 필드 저장 및 표시, 현재 기본값은 `Python`
- 대회형 manifest 확장을 고려한 구조 포함
- 대회형 기록에서는 문제별 제출 횟수, 첫 AC까지 제출 횟수, 패널티, 점수, 맞은 문제 수, 총 패널티 표시 가능

## 수업 결과 확인

주요 파일:

- `LessonResultInspectionReader.cs`
- `MainWindow.xaml`
- `MainWindow.xaml.cs`
- `SubmissionHistoryFileWindow.xaml`
- `SubmissionHistoryFileWindow.xaml.cs`

구현 상태:

- `수업` 메뉴에 `수업 결과 확인하기...`가 추가되었습니다.
- 학생이 제출한 수업 작업 폴더 ZIP을 교사가 읽기 전용으로 확인하는 기능입니다.
- ZIP 내부에서 문제 JSON과 `.localjudge/submissions/` 아래 제출 JSON을 읽습니다.
- 최소 1개 이상의 유효한 문제 JSON이 없으면 `잘못된 파일입니다.`를 표시합니다.
- 문제는 있지만 `.localjudge/submissions/`가 없거나 유효 제출 기록이 없으면 `제출 기록이 없습니다.`를 표시합니다.
- 유효한 수업 결과 ZIP은 기존 제출 이력 확인 창 모델로 변환해 표시합니다.
- 문항 요약 행 색상:
  - 제출 없음: 기본 검정
  - 제출 있음, AC 없음: 빨간색
  - AC 있음: 초록색
- AC가 없는 문항은 제목 뒤에 마지막 판정을 붙입니다.
  - 예: `A+B (WA)`
  - 예: `최단거리 (TLE)`
- `SubmissionAttemptDocument`에 수업 제출 확장을 위한 필드가 추가되었습니다.
  - `LessonId`
  - `LessonTitle`
  - `SectionTitle`
  - `ProblemRelativePath`

## 현재 주의 사항

- `Editor\monaco`는 외부 번들 성격이 강하므로 직접 작성 코드와 구분해서 봐야 합니다.
- `ProblemViewer`의 Markdown/LaTeX 렌더러는 현재 최소 구현입니다.
- 외부 URL 이미지는 오프라인 안정성을 위해 막는 방향입니다.
- 기본 빌드가 파일 잠금으로 실패하면 실행 중인 Local Judge 또는 Visual Studio 디버깅 세션을 종료하거나 임시 `OutputPath` 빌드를 사용합니다.
- Codex 인증 파일은 다른 PC로 옮기면 안 됩니다.
  - 옮기면 안 되는 예: `C:\Users\kimsd\.codex\auth.json`, `.sandbox-secrets`
- 대화 세션 원본은 `.codex\sessions\...jsonl`에 있을 수 있지만, 다른 PC에서 그대로 이어 열리는 것은 보장되지 않습니다.
- 이 `handoff.md`를 기준으로 이어가는 것이 더 안정적입니다.

## 다른 PC에서 이어가는 방법

1. 저장소를 같은 커밋 상태로 가져옵니다.

```powershell
git status --short
git log -1 --oneline
```

2. 솔루션을 빌드합니다.

```powershell
dotnet build "Local Judge\Local Judge.sln" --no-restore
```

3. 출력 파일 잠금 문제가 있으면 임시 출력 경로로 검증합니다.

```powershell
dotnet build "Local Judge\Local Judge.sln" --no-restore -p:OutputPath="$env:TEMP\localjudge-build\"
```

4. 수동 확인 흐름:

- 프로그램 시작 시 벤치마크 실행 확인
- 문제 불러오기
- 새 문제 만들기
- Markdown/LaTeX 미리보기 확인
- 이미지 삽입 후 저장
- 저장한 문제 다시 불러오기
- 예제 실행
- 제출
- 제출 이력 보기
- 제출 이력 내보내기
- 제출 이력 파일 확인하기
- 수업 결과 확인하기

## 다음 작업 후보

우선순위가 높은 후보:

- 수업 열기 및 수업별 제출 저장
  - 현재 구현된 것은 교사용 `수업 결과 확인하기`입니다.
  - 학생이 수업 ZIP을 열고 풀어가는 `수업 열기`와 수업 폴더 내부 `.localjudge/submissions/` 저장 흐름은 다음 단계로 남아 있습니다.
- KaTeX 오프라인 번들 적용
  - 현재 자체 LaTeX 렌더러를 KaTeX로 교체하거나 보완
  - `cases`, `align`, 중첩 분수 등 복잡한 수식 지원
- 문항 패키지 ZIP 불러오기/내보내기
  - 현재는 JSON + `.assets` 폴더 구조
  - 향후 `problem.json` + `assets/` ZIP 구조로 확장하면 다른 PC 이동이 쉬워짐
- 문항 편집기 UX 개선
  - asset 목록 관리 UI
  - 미리보기 갱신 상태 표시
  - 현재 커서 위치 기반 이미지 삽입 세부 개선
- 렌더러 테스트 추가
  - Markdown/LaTeX/image 렌더링 케이스를 JS 단위로 검증
- 다중 언어 제출 지원
  - 현재 제출 언어 필드는 있지만 실제 실행은 Python 중심

## 코드 규모

직접 작성 소스 기준:

- 약 5,517줄
- 대상: `.cs`, `.xaml`, `.js`, `.css`, `.csproj`
- 제외: `bin`, `obj`, `Editor\monaco`

외부 번들까지 포함한 `bin/obj` 제외 전체 기준:

- 약 72,058줄
