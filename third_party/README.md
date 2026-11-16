# Filee document conversion runtime

모두찾아줘의 문서 변환은 [KnifeLemon/Filee](https://github.com/KnifeLemon/Filee)의 공식 Windows x64 CLI를 사용합니다. Filee의 문서 변환 엔진과 경로 선택을 그대로 실행하며, 모두찾아줘는 파일 선택·형식 선택·진행 표시·안전한 결과 폴더·취소를 연결합니다.

- Pin: `filee.json`의 공식 v2.4.1 릴리스, 소스 커밋과 SHA-256.
- Filee: MIT, Copyright (c) 2026 Myeonggu Kim (KnifeLemon) and Filee contributors.
- 실행 파일/라이브러리/엔진은 수정하거나 다른 서명으로 바꾸지 않습니다.
- Filee GUI·탐색기 확장·PATH 연결과 디버그 심벌은 배포하지 않습니다. CLI와 종속 런타임 및 기본 엔진을 포함합니다.
- 앱 설치 시 `conversion/filee-2.4.1`에 포함합니다. 사용자 PC의 별도 Filee·Office·한글 설치가 필요하지 않습니다.
- Filee 설정과 추가 엔진은 `FILEE_DATA_DIR`을 통해 모두찾아줘 데이터 폴더의 `conversion-filee`에 격리합니다. 기존 Filee 설치의 설정이나 엔진을 변경하지 않습니다.
- 배포물에 `FILEE-LICENSE.txt`, `FILEE-THIRD-PARTY-NOTICES.md`, .NET 런타임 고지와 기본 엔진의 원본 라이선스를 포함합니다. 라이브러리별 이용 조건과 소스 링크는 Filee 원본 고지를 따릅니다.
- LibreOffice·Pandoc·calibre는 사용자가 선택할 때 Filee의 공식 고정 다운로드와 SHA-256 검증으로 추가합니다. 이 프로그램들은 각자의 라이선스로 별도 실행됩니다.

빌드: `python tools/prepare_filee.py --publish-dir artifacts/app`. 공식 ZIP은 빌드 캐시에만 저장하며 Git에 넣지 않습니다.
