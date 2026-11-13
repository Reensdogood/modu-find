"""Small synthetic documents; no personal files or third-party test data."""
import pathlib, zipfile

def make_zip(path,entries):
    with zipfile.ZipFile(path,'w',compression=zipfile.ZIP_DEFLATED) as z:
        for name,text in entries.items():z.writestr(name,text.encode('utf-8'))

def make_documents(folder):
    folder=pathlib.Path(folder);folder.mkdir(parents=True,exist_ok=True)
    make_zip(folder/'공내역서.xlsx',{
        'xl/workbook.xml':'<workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><sheets><sheet name="기능점수목록" r:id="rId1"/></sheets></workbook>',
        'xl/_rels/workbook.xml.rels':'<Relationships><Relationship Id="rId1" Target="worksheets/sheet1.xml"/></Relationships>',
        'xl/worksheets/sheet1.xml':'<worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><sheetData><row r="1"><c r="A1" t="inlineStr"><is><t>관악 SW FP 개발비</t></is></c></row></sheetData></worksheet>'})
    make_zip(folder/'설치.hwpx',{'Contents/section0.xml':'<root><p><run><t>교육방 설치 보고서</t></run></p></root>'})
    make_zip(folder/'보고서.docx',{'word/document.xml':'<document><p><r><t>시험용 워드 문서</t></r></p></document>'})
    make_zip(folder/'발표.pptx',{'ppt/slides/slide1.xml':'<slide><p><r><t>시험용 발표 자료</t></r></p></slide>'})
    (folder/'관악_메모.txt').write_text('관악 소프트웨어 FP 산출내역',encoding='utf-8')
    (folder/'손상.pdf').write_bytes(b'not a PDF')
    with (folder/'대용량.txt').open('wb') as f:f.truncate(81*1024*1024)
    return folder

def make_pdf(path,image_only=False):
    content=b'q 100 0 0 100 10 10 cm /Im1 Do Q' if image_only else b'BT /F1 12 Tf 30 160 Td (ModuFind FP sample) Tj ET'
    resources=b'/XObject << /Im1 6 0 R >>' if image_only else b'/Font << /F1 4 0 R >>'
    objects=[b'<< /Type /Catalog /Pages 2 0 R >>',b'<< /Type /Pages /Kids [3 0 R] /Count 1 >>',
        b'<< /Type /Page /Parent 2 0 R /MediaBox [0 0 300 200] /Resources << '+resources+b' >> /Contents 5 0 R >>',
        b'<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>',
        b'<< /Length '+str(len(content)).encode()+b' >>\nstream\n'+content+b'\nendstream']
    if image_only:objects.append(b'<< /Type /XObject /Subtype /Image /Width 1 /Height 1 /ColorSpace /DeviceRGB /BitsPerComponent 8 /Length 3 >>\nstream\n\xff\x00\x00\nendstream')
    output=bytearray(b'%PDF-1.4\n');offsets=[0]
    for number,obj in enumerate(objects,1):
        offsets.append(len(output));output.extend(f'{number} 0 obj\n'.encode()+obj+b'\nendobj\n')
    xref=len(output);output.extend(f'xref\n0 {len(offsets)}\n0000000000 65535 f \n'.encode())
    for offset in offsets[1:]:output.extend(f'{offset:010d} 00000 n \n'.encode())
    output.extend(f'trailer\n<< /Size {len(offsets)} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n'.encode())
    pathlib.Path(path).write_bytes(output)
