# Local Judge Handoff

작성일: 2026-05-29  
작업 위치: `C:\Users\kimsd\source\repos\celbeing\Class-Code`  
현재 솔루션: `Local Judge\Local Judge.sln`  
브랜치: `main`  
기준 커밋: `d3d7a21 문항 편집기 개선 - 이미지 삽입 가능 - LaTex 수식 입력 가능 - 블록 입력 가능`

## 현재 상태

- `Local Judge`가 현재 작업 중인 솔루션이다.
- `Class_code`는 만들다 중단한 이전 솔루션으로, 현재 기능 개발 대상이 아니다.
- 이 handoff 작성 직전 `git status --short`와 `git diff --stat`은 비어 있었다. 즉 코드 변경사항은 기준 커밋에 반영된 상태로 보인다.
- 검증 명령:

```powershell
dotnet build "Local Judge\Local Judge.sln" --no-restore -p:OutputPath="C:\Users\kimsd\AppData\Local\Temp\localjudge-build\"
```

- 위 빌드는 통과했다. 경고 0개, 오류 0개.
- 기본 `bin\Debug` 출력 경로 빌드는 실행 중인 `Local Judge.exe`나 Visual Studio가 파일을 잠그면 실패할 수 있다. 이 경우 앱을 종료하거나 위처럼 임시 `OutputPath`를 지정해서 컴파일 검증하면 된다.

## 앱 개요

`Local Judge`는 온라인 저지의 불편함을 보완하기 위한 WPF 기반 로컬 저지 앱이다.

- .NET 8 WPF 앱이다.
- 코드 편집기는 WebView2 + Monaco Editor를 사용한다.
- 현재 실행 언어는 Python 중심이다.
- 프로그램 시작 시 채점 환경 벤치마크를 필수 실행한다.
- 벤치마크 결과를 문항의 이상적 시간/메모리 제한에 반영해 로컬 적용 제한을 계산한다.
- 예제 실행, 제출, 채점 결과 표시, 제출 이력 저장/조회/내보내기/가져오기 기능이 있다.

## 주요 구현 상태

### Python 실행 및 채점

- `PythonRunner.cs`가 Python 실행을 담당한다.
- 지원 제한:
  - 시간 제한
  - 출력 제한
  - 메모리 제한
- 실행 결과는 `AC`, `WA`, `TLE`, `MLE`, `RE`, `OLE` 등으로 분류된다.
- 터미널 출력에서 `PASS`, `FAIL`, `AC`, `WA`, `TLE`, `MLE`, `RE`, `OLE` 토큰은 색상 강조된다.
- 실행 중 중지 버튼도 연결되어 있다.

### 채점 환경 벤치마크

- `JudgeEnvironmentBenchmark.cs`가 담당한다.
- 앱 시작 시 필수 실행된다.
- 사용자가 메뉴에서 다시 실행할 수 있다.
- 상태 표시줄에는 채점 관련 상태만 표시한다.
- 벤치마크 중에는 예제 실행/제출이 비활성화된다.
- 벤치마크 결과는 시간 배율, 추가 시간, 추가 메모리를 계산해 채점 제한에 반영한다.

### 문항 스키마

- `ProblemDocument.cs`가 문항 JSON 스키마다.
- 현재 핵심 필드:
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
- 기존 문제는 `version: 3`, 평문 중심이다.
- 새 문제는 `version: 4`, `statementFormat: "markdown-latex"`로 저장된다.
- 기존 JSON 문제는 계속 불러올 수 있다.

### 문항 편집기

- 주요 파일:
  - `ProblemEditorWindow.xaml`
  - `ProblemEditorWindow.xaml.cs`
  - `ProblemAssetUtilities.cs`
- 편집기는 좌우 분할 구조다.
  - 왼쪽: 문제 정보, Markdown/LaTeX 본문 입력, 예제, 테스트 ZIP
  - 오른쪽: WebView2 미리보기
- 새 문제 생성 시 저장 버튼은 파일 저장 대화상자를 열고 JSON 저장만 한다.
- 새 문제 생성 후 메인 윈도우에 자동으로 불러오지 않는다.
- 기존 문제 수정 시 제작자/출처는 읽기 전용이다.
- 새 문제 생성 시 제작자/출처는 필수이며, 이후 변경할 수 없다는 안내가 표시된다.
- 예제는 추가/삭제 가능하다.
- 예제 1은 삭제 불가이고, 예제 2부터 삭제 버튼이 보인다.
- 채점 테스트는 ZIP 파일로 등록한다.
  - `.in` 파일과 같은 이름의 `.out` 파일을 매칭한다.
  - 이름 규칙은 엄격하지 않다.
  - 매칭되지 않는 `.in` 또는 `.out`이 있으면 에러 메시지를 띄운다.

### 문항 이미지 asset

- 이미지 삽입을 지원한다.
- 지원 확장자:
  - `png`
  - `jpg`
  - `jpeg`
  - `gif`
  - `webp`
- 이미지는 JSON에 base64로 넣지 않는다.
- 문제 JSON 옆에 `{문제파일명}.assets` 폴더를 만들고 이미지 파일을 저장한다.
- Markdown 본문에는 다음 형태로 삽입된다.

```markdown
![image-name](assets/image-name.png)
```

- `ProblemAssetUtilities.cs`가 asset 폴더 경로, 파일명 충돌 방지, 복사, content type 등을 처리한다.
- 다른 이름으로 문제 저장 시 asset 폴더도 함께 복사되도록 되어 있다.
- 편집기에 `사용하지 않는 이미지 정리` 버튼이 있다.

### 문항 표시 UI

- 메인 창의 좌측 문제 표시 영역은 기존 TextBox 묶음에서 WebView2 문제 뷰어로 교체되었다.
- 주요 파일:
  - `ProblemViewer\index.html`
  - `ProblemViewer\problemViewer.css`
  - `ProblemViewer\problemViewerHost.js`
- `Local Judge.csproj`에서 `ProblemViewer\**\*.*`를 Content로 포함한다.
- 메인 화면과 편집기 미리보기가 같은 viewer를 사용한다.
- 표시 순서:
  - 제목
  - 메타 정보
  - 문제 설명
  - 입력
  - 출력
  - 모든 예제 입력/출력
- 예제는 첫 번째만 표시하지 않고 전체 sample을 렌더링한다.
- 이미지 로드는 WebView2 virtual host mapping으로 `assets/`만 허용한다.

### Markdown/LaTeX 지원 범위

현재 `ProblemViewer\problemViewerHost.js`의 자체 렌더러가 처리한다.

지원 Markdown:

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

지원 LaTeX:

- 인라인 수식: `$A+B$`
- 블록 수식:

```latex
$$
A_i \leq B_i
$$
```

- 지원 명령 일부:
  - `\leq`, `\le`, `\geq`, `\ge`
  - `\neq`, `\ne`
  - `\times`, `\cdot`, `\pm`
  - `\infty`, `\ldots`, `\dots`
  - `\sum`, `\prod`
  - `\alpha`, `\beta`, `\gamma`, `\delta`, `\epsilon`, `\theta`, `\lambda`, `\mu`, `\pi`, `\sigma`, `\phi`, `\omega`
  - `\rightarrow`, `\to`
- 지원 구조:
  - `\frac{a}{b}`
  - `\sqrt{x}`
  - `x^2`, `x^{n+1}`
  - `a_i`, `a_{i+1}`
- 제한:
  - KaTeX/MathJax 수준의 전체 LaTeX가 아니다.
  - 중첩 분수, `align`, `cases`, `matrix`, `\left`, `\right` 등은 아직 미지원이다.
  - 복잡한 수식이 필요하면 다음 단계에서 KaTeX 오프라인 번들을 붙이는 것이 맞다.

## 제출 이력 기능

주요 파일:

- `SubmissionHistoryStore.cs`
- `SubmissionHistoryWindow.xaml`
- `SubmissionHistoryWindow.xaml.cs`
- `SubmissionHistoryExporter.cs`
- `SubmissionHistoryImportReader.cs`
- `SubmissionHistoryFileWindow.xaml`
- `SubmissionHistoryFileWindow.xaml.cs`

구현 상태:

- 제출 시도는 Local Judge 실행 위치 기준의 제출 이력 저장소에 저장된다.
- 제출 이력 조회 UI가 있다.
- 제출 이력 ZIP 내보내기가 있다.
- 제출 이력 ZIP 확인 UI가 있다.
- `저지` 메뉴에 `제출 이력 파일 확인하기...`가 있다.
- 내보낸 JSON/ZIP은 향후 대회 단위 제출 이력으로 확장할 수 있도록 manifest 구조가 확장되어 있다.
- 대회 형식 manifest가 있으면 문제별 제출 횟수, 첫 AC까지 제출 횟수, 패널티, 점수, 맞은 문제 수, 총 패널티를 계산해 표시한다.
- 제출 언어 필드는 현재 `Python`으로 저장/표시한다.

## 중요한 주의사항

- `Editor\monaco`는 외부 번들 성격이 강하다. 코드 줄 수나 리뷰에서 직접 작성 코드와 구분해야 한다.
- `ProblemViewer`의 Markdown/LaTeX 렌더러는 직접 구현한 최소 기능이다. 보안상 임의 HTML 실행을 허용하지 않는 방향이다.
- 외부 URL 이미지는 v1에서 지원하지 않는 방향이다. 오프라인 안정성을 위해 문항 asset 폴더만 사용한다.
- 기본 빌드 출력 파일이 잠기는 문제가 있으면 Local Judge 실행 프로세스와 Visual Studio 디버깅 세션을 종료한다.
- `.codex\auth.json`, `.sandbox-secrets` 등 인증/민감 정보는 다른 PC로 옮기지 않는다.
- 대화 원본은 `C:\Users\kimsd\.codex\sessions\...jsonl`에 있을 수 있지만, 다른 PC에서 그대로 이어 열리는 것은 보장되지 않는다. 이 파일이 실질적인 인수인계 기준이다.

## 다른 PC에서 이어가는 방법

1. 저장소를 같은 커밋 상태로 가져온다.

```powershell
git status --short
git log -1 --oneline
```

2. 기준 커밋이 다음인지 확인한다.

```text
d3d7a21 문항 편집기 개선 - 이미지 삽입 가능 - LaTex 수식 입력 가능 - 블록 입력 가능
```

3. 빌드한다.

```powershell
dotnet build "Local Judge\Local Judge.sln" --no-restore
```

4. 출력 파일 잠금으로 실패하면 임시 출력 경로로 검증한다.

```powershell
dotnet build "Local Judge\Local Judge.sln" --no-restore -p:OutputPath="$env:TEMP\localjudge-build\"
```

5. 앱을 실행해 확인할 핵심 흐름:
   - 프로그램 시작 시 벤치마크 실행
   - 문제 불러오기
   - 새 문제 만들기
   - Markdown/LaTeX 미리보기
   - 이미지 삽입 후 저장
   - 저장한 문제 다시 불러오기
   - 예제 실행
   - 제출
   - 제출 이력 보기
   - 제출 이력 내보내기
   - 제출 이력 파일 확인하기

## 다음 작업 후보

우선순위가 높은 후보:

- KaTeX 오프라인 번들 적용
  - 현재 자체 LaTeX 렌더러를 KaTeX로 교체하거나 보완한다.
  - `\begin{cases}`, `align`, 중첩 분수 등 복잡한 수식 지원이 가능해진다.
- 문항 패키지 ZIP 불러오기/내보내기
  - 현재는 JSON + `.assets` 폴더 구조다.
  - 향후 `problem.json` + `assets/` 구조의 ZIP으로 확장하면 다른 PC 이동이 쉬워진다.
- 문항 편집기 UX 개선
  - 미리보기 debounce, 커서 위치 이미지 삽입, asset 목록 관리 UI를 더 다듬을 수 있다.
- 렌더러 테스트 추가
  - Markdown/LaTeX/image 렌더링 케이스를 최소한 JS 단위로 검증하면 좋다.
- 다중 언어 제출 지원
  - 현재 제출 언어 필드는 있지만 실제 실행은 Python 중심이다.

## 최근 확인한 규모

직접 작성 소스 기준 대략 5,517줄이었다.  
Monaco 번들 같은 외부 파일까지 포함하면 `bin/obj` 제외 기준 약 72,058줄이었다.

