# Local Judge for Python

Python 풀이를 로컬 PC에서 실행하고 채점할 수 있는 Windows용 로컬 저지 프로그램입니다. 온라인 저지 환경을 그대로 대체하기보다는, 수업과 대회에서 사용할 수 있도록 문제 배포, 제출 기록 관리, 수업/대회 패키지 운영 기능을 함께 제공합니다.

## Download

| Version | Date | Installer | Notes |
| --- | --- | --- | --- |
| v1.0 | 2026-06-23 | [LocalJudgeSetup-v1.0.exe](https://github.com/celbeing/Class-Code/raw/827d5008e6e2be2dad34f82f5c2a1742256a3380/installer/output/LocalJudgeSetup-v1.0.exe) | Python 3.13 내장, WebView2 Runtime bootstrapper 포함 |
| v1.1 | 2026-07-01 | [LocalJudgeSetup-v1.1.exe](https://github.com/celbeing/Local-Judge-for-Python/raw/refs/heads/main/installer/output/LocalJudgeSetup-v1.1.exe) | Monaco Editor 테마 추가, 초기 코드 설정 기능 추가 |


## 주요 기능

- Python 코드 실행 및 채점
- 예제 실행, 제출 채점, PASS/FAIL/AC/WA/TLE/MLE/RE/OLE 결과 표시
- 시간 제한, 메모리 제한, 출력 제한 적용
- 실행 환경 벤치마크를 통한 PC별 추가 시간/메모리 보정
- Python 3.13.14 embeddable runtime 내장
- 사용자가 직접 지정한 외부 Python 경로 저장 및 재사용

## 문항 관리

- JSON 기반 문제 생성, 수정, 불러오기
- 문제 제작자와 출처 저장
- Markdown/LaTeX 기반 문제 설명 표시
- 이미지/asset 포함 문제 지원
- sample 추가/삭제
- 채점용 testcase ZIP 등록
- `.in` 파일과 이름이 같은 `.out` 파일 자동 매칭

## 제출 기록

- 문제별 제출 이력 저장
- 제출 결과, 실행 시간, 메모리, testcase별 결과 기록
- 제출 이력 조회 UI
- 제출 이력 ZIP 내보내기
- 내보낸 제출 이력 파일 확인 UI

## 수업 기능

- 여러 문항을 폴더/ZIP 구조로 묶어 수업으로 열기
- 수업 내 문제별 제출 기록을 별도로 저장
- 문제별 풀이 상태 표시
- 수업 결과 ZIP 내보내기
- 교사용 수업 결과 확인 UI

## 대회 기능

- 대회 만들기: 대회명, 시작/종료 시간, 추가 정보, 문항, 풍선 색 설정
- 문항을 A, B, C 순서로 라벨링
- 시작 전 문제 열람 제한 및 대회 정보 화면 표시
- 종료 이후 제출 제한
- 종료된 대회도 다시 열어 결과 내보내기 가능
- ICPC 방식 패널티 계산
- 제출 시각 기준 초 단위 패널티 반영
- 대회 문항별 코드 draft 자동 저장
- 대회 제출 기록 유지 및 결과 ZIP 내보내기
- 대회 testcase 암호화 저장 및 4자리 PIN 기반 복호화

## 배포 구성

설치파일은 Inno Setup으로 생성합니다.

- 앱 본체
- .NET self-contained publish 결과물
- 내장 Python 3.13.14 runtime
- Editor/ProblemViewer 리소스
- WebView2 Evergreen Bootstrapper

WebView2 Runtime이 이미 설치된 PC에서는 건너뛰고, 없는 PC에서는 설치 중 bootstrapper가 실행됩니다. Bootstrapper 방식이므로 WebView2가 없는 PC에서는 설치 시 인터넷 연결이 필요합니다.
