# Local Judge for Python v1.1

Python 풀이를 로컬 PC에서 실행하고 채점할 수 있는 Windows용 로컬 저지 프로그램입니다. 온라인 저지 환경을 그대로 대체하기보다는, 수업과 대회에서 사용할 수 있도록 문제 배포, 제출 기록 관리, 수업/대회 패키지 운영 기능을 함께 제공합니다.

## Download

| Version | Date | Installer | Notes |
| --- | --- | --- | --- |
| v1.0 | 2026-06-23 | [LocalJudgeSetup-v1.0.exe](https://github.com/celbeing/Class-Code/raw/827d5008e6e2be2dad34f82f5c2a1742256a3380/installer/output/LocalJudgeSetup-v1.0.exe) | Python 3.13 내장, WebView2 Runtime bootstrapper 포함 |
| v1.1 | 2026-07-01 | [LocalJudgeSetup-v1.1.exe](https://github.com/celbeing/Local-Judge-for-Python/raw/refs/heads/main/installer/output/LocalJudgeSetup-v1.1.exe) | Monaco Editor 테마 추가, 초기 코드 설정 기능 추가 |

## Sample Problem Set
|No|Subject|Download|
| --- | --- | --- |
|(1)|변수와 출력|[Download](https://github.com/celbeing/Local-Judge-for-Python/raw/refs/heads/main/Local%20Judge/ExampleLessons/1.%20%EB%B3%80%EC%88%98%EC%99%80%20%EC%B6%9C%EB%A0%A5.zip)|
|(2)|자료형과 사칙연산|[Download](https://github.com/celbeing/Local-Judge-for-Python/raw/refs/heads/main/Local%20Judge/ExampleLessons/2.%20%EC%9E%90%EB%A3%8C%ED%98%95%EA%B3%BC%20%EC%82%AC%EC%B9%99%EC%97%B0%EC%82%B0.zip)|
|(3)|입출력 처리와 조건문|[Preparing]|
|(4)|반복문 for, range|[Preparing]|
|(5)|반복과 조건 while|[Preparing]|
|(6)|리스트와 index|[Preparing]|
|(7)|정렬과 탐색|[Preparing]|
|(8)|브루트포스, DFS|[Preparing]|
|(9)|큐, BFS|[Preparing]|

## 주요 기능

- Python 코드 실행 및 채점
- 예제 실행, 제출 채점, PASS/FAIL/AC/WA/TLE/MLE/RE/OLE 결과 표시
- 시간 제한, 메모리 제한, 출력 제한 적용
- 실행 환경 벤치마크를 통한 PC별 추가 시간/메모리 보정
- Python 3.13.14 embeddable runtime 내장
- 사용자가 직접 지정한 외부 Python 경로 저장 및 재사용

### 문제 불러오기
<img width="1186" height="793" alt="Image" src="https://github.com/user-attachments/assets/51ec6333-e7c5-47b5-9574-af781fd8a93e" />

- 좌측 탭 상단의 `불러오기` 버튼으로  JSON 형식의 문제 파일을 불러올 수 있습니다.
- 불러온 문제에 대해 우측 탭에 작성된 코드를 제출하거나 문제에 저장되어있는 예제를 실행해볼 수 있습니다.
- 좌측 탭 하단의 `입력` 탭에 미리 입력 스트림을 준비해두고 `실행` 버튼을 누르면 편집기의 코드가 실행된 결과를 `터미널` 탭에서 확인할 수 있습니다.
- 이전에 작성하던 코드는 자동 저장되며 다른 문제를 열어보거나 프로그램을 종료하고 다시 열어도 작성하던 코드를 이어서 작성할 수 있습니다.

### 제출 이력 확인
<img width="986" height="673" alt="Image" src="https://github.com/user-attachments/assets/3a4d029f-2274-412e-a934-f58431890854" />

- 상단 메뉴의 \[저지]-\[제출 이력 보기...]를 선택하면 현재 열려있는 문제의 제출 이력을 확인할 수 있습니다.
- 제출했던 코드와 각 tc별 실행 시간과 메모리, 스트림 출력 결과, 에러 등을 확인할 수 있습니다.

### 문제 만들기
<img width="1166" height="813" alt="Image" src="https://github.com/user-attachments/assets/eed87a3e-6ab8-4f6f-8f09-8dc1ae868e8b" />

- 새 문제를 직접 만들 수 있습니다.
- 시간 제한과 메모리 제한을 정할 수 있습니다.
- 채점 테스트 케이스는 하단의 `ZIP 불러오기...` 버튼을 통해 불러올 수 있습니다. `{tc 번호}.in` 과 `{tc 번호}.out` 쌍으로 된 파일을 `.zip` 파일로 압축해 업로드하면 등록됩니다.
- 초기 코드 설정 체크박스를 선택하면 문항의 초기 코드를 설정할 수 있습니다. 문제를 열어볼 때 설정된 초기 코드가 작성되어있는 상태로 열리게 됩니다.

### 수업 만들기
<img width="1186" height="793" alt="Image" src="https://github.com/user-attachments/assets/ecf67a68-4e5d-4ed3-81a9-71aed2b628ff" />

- 폴더 구조를 만들어 JSON 문제 파일을 내부에 배치하고 최상위 폴더를 `.zip` 파일로 압축하여 수업을 만들 수 있습니다.
- 상단 메뉴의 \[수업]-\[수업 열기...]를 선택하여 수업을 열어볼 수 있습니다.
- 선택한 파일 내부의 폴더 구조에 따라 좌측 탭 상단에 문제들을 확인할 수 있습니다.
- 각 문제의 채점 상태가 색깔과 제출 결과로 표기됩니다.

### 기타
- 채점 환경에 따라 자원 제한이 불리하게 작용할 수 있으므로 프로그램 실행 시 벤치마크를 실행하여 기본 자원에 추가 시간, 추가 메모리를 부여합니다.
- python 인터프리터가 내장되어있어 별도의 설치가 필요하지 않습니다. 다른 버전의 python 인터프리터를 설치해 연결할 수 있습니다.
- 처음 문제를 열었을 때 편집기에 초기 코드가 작성되어 있습니다. 상단 메뉴의 [도구]-[기본 코드 설정]에서 변경할 수 있습니다.
- [도구]-[환경설정]에서 Monaco 에디터의 테마를 변경할 수 있습니다.
- 디버그 기능은 추후 추가될 예정입니다.
- 대회는 ICPC 룰을 적용하며 시스템 시각을 기준으로 제출이 열리고 닫힙니다.

## 배포 구성

설치파일은 Inno Setup으로 생성합니다.

- 앱 본체
- .NET self-contained publish 결과물
- 내장 Python 3.13.14 runtime
- Editor/ProblemViewer 리소스
- WebView2 Evergreen Bootstrapper

WebView2 Runtime이 이미 설치된 PC에서는 건너뛰고, 없는 PC에서는 설치 중 bootstrapper가 실행됩니다. Bootstrapper 방식이므로 WebView2가 없는 PC에서는 설치 시 인터넷 연결이 필요합니다.
