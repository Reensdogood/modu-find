# 모두찾아줘 검증

## 1.2.1 · 버튼과 체크박스 표시

검증일: 2026-11-16. 사용자 스크린샷의 세 영역을 대상으로 native Windows 화면에서 확인.

- 상단 ‘문서 변환’·‘업데이트 확인’의 전체 글자와 테두리가 헤더 안에 표시됨.
- ‘본문 전체 보기’가 독립 행에 표시되며, 검색 창을 최소 높이로 줄여도 계속 보임. 상세 내용은 별도로 스크롤할 수 있음.
- 변환 상태 안내와 실행 버튼 행을 분리함. 변환 창을 최소 폭·최소 높이로 줄여도 ‘변환 시작’과 다른 실행 버튼의 전체 글자가 표시됨.
- 검색·변환 로직과 데이터 형식은 변경하지 않음. 태그 릴리스 CI에서 기존 기능과 설치 수명주기 검사를 실행함.

## 1.2.0 · 문서 변환

검증일: 2026-11-16. Windows x64, Filee 2.4.1의 고정된 공식 CLI 배포본 사용.

- 기존 검색·추출·업데이트 검사 45개, 작업 프로세스 4개, 통합 색인 7개 통과.
- 실제 변환 통합 검사 40개 통과: 한글 TXT→DOCX→HWPX→HWP→TXT, HWPX→PDF, PDF→DOCX, CSV↔XLSX, PDF→PPTX→PDF, HWPX→PNG 결과를 파일 구조·내용으로 확인.
- 같은 이름의 입력 파일 분리, 원본 SHA-256·수정일 보존, 손상 PDF의 실패 처리, 취소 시 임시 결과 정리와 완료 결과 보존, Filee 데이터 경로 분리 확인.
- 별도 임시 경로에서 Pandoc을 실제 설치하고 DOCX→RTF를 변환하는 확장 검사 43개 통과. LibreOffice·calibre 다운로드 설치는 이번 로컬 검증 범위에 포함하지 않음.
- 별도 검색 데이터 경로로 실행한 GUI에서 파일 추가, 공통 출력 형식 조회, 저장 폴더 선택, TXT→PDF 실행과 ‘완료 1개·실패 0개’ 표시 확인. 작은 창에서 하단 상태·버튼 표시 확인.
- 빌드 시 Filee ZIP의 고정 SHA-256을 검사하며 원본 라이선스·제3자 고지·출처 정보를 배포물에 포함.
- 태그 릴리스 CI는 새 Windows 환경에서 위 검사와 설치·재설치·제거 검사 7개를 실행한 뒤 설치 파일을 공개함.

```powershell
dotnet run --project DocumentFinder.Conversion.Tests -c Release -- artifacts/app/conversion/filee-2.4.1
# 선택 엔진 다운로드까지 확인할 때:
dotnet run --project DocumentFinder.Conversion.Tests -c Release -- artifacts/app/conversion/filee-2.4.1 --install-pandoc
```

## 1.1.0 · 검색과 설치

검증일: 2026-11-13. Windows x64, .NET 8 자체 포함 배포.

- 검색·문서 추출·저장·갱신 검사와 GitHub 업데이트 정책/다운로드 검사 45개 통과.
- 단일 실행 파일의 숨김 작업 프로세스 검사 4개 통과: 텍스트 PDF, 이미지 전용 PDF의 OCR 필요 표시, 없는 파일 오류, 오류 뒤 다음 파일 처리.
- 합성 문서 7개로 통합 색인 검사 7개 통과: 여러 형식 추출, 손상 문서 분리, 용량 제한, 일반 파일명 엑셀 본문 검색, 변경 없는 파일 건너뛰기, 변경 내용 갱신, 취소 시 색인 보존.
- 임시 경로에서 설치·재설치·제거 검사 7개 통과: 올바른 앱 이름/버전 등록, 제거 프로그램 등록, 업데이트 재설치, 관리 파일/등록 정보 제거, 관계없는 사용자 파일 보존, 기존 검색 데이터베이스의 SHA-256 불변 확인.
- 실제 설치 후 기존 문서 목록과 본문이 유지됨을 확인. GUI에서 ‘관악 FP’ 검색 결과와 일치 내용 미리보기 확인.
- 아이콘 ICO에 16·24·32·48·64·128·256px 포함. 실행 파일, 설치 프로그램, 창 제목, 바탕화면/시작 메뉴에 같은 아이콘 적용.
- 기존 데이터 위치를 유지하고, 배포 파일/공개 소스에는 개인 문서·색인·인증 정보를 넣지 않음.

명령:

```powershell
dotnet run --project DocumentFinder.Tests -c Release
python verify_runtime.py artifacts/app/ModuFind.exe
python verify_index.py artifacts/app/ModuFind.exe
python verify_installer.py artifacts/release/ModuFind-Setup-1.1.0-win-x64.exe
```

설치 검사는 같은 AppId로 이미 설치된 프로그램이 있으면 중단한다. 실제 사용자 설치를 테스트 경로로 덮어쓰지 않기 위한 조치다. Windows CI의 새 계정이나 설치 전 환경에서 실행한다.

테스트 문서는 코드에서 생성하며 개인 파일에 의존하지 않는다. GitHub 업데이트 검사는 가짜 HTTP 응답으로 새 버전/다운그레이드/초안/시험 버전/HTTP 오류/잘못된 URL/크기 제한/취소/SHA-256 오류를 확인한다. 공개 배포 후에는 실제 릴리스 API와 설치 파일의 다운로드·SHA-256도 별도로 확인한다.
