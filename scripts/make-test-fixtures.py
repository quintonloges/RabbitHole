"""Small generated document fixtures. Never loaded by the production application."""
from pathlib import Path
from zipfile import ZipFile, ZIP_STORED

root = Path(__file__).resolve().parents[1] / "tests" / "e2e" / "fixtures"
root.mkdir(parents=True, exist_ok=True)
with ZipFile(root / "test-book.epub", "w", compression=ZIP_STORED) as z:
    z.writestr("mimetype", "application/epub+zip")
    z.writestr("META-INF/container.xml", '<container xmlns="urn:oasis:names:tc:opendocument:xmlns:container"><rootfiles><rootfile full-path="book.opf"/></rootfiles></container>')
    z.writestr("book.opf", '<package xmlns="http://www.idpf.org/2007/opf"><metadata xmlns:dc="http://purl.org/dc/elements/1.1/"><dc:title>TEST FIXTURE: Private book</dc:title><dc:creator>Fixture author</dc:creator></metadata><manifest><item id="one" href="one.xhtml" media-type="application/xhtml+xml"/><item id="two" href="two.xhtml" media-type="application/xhtml+xml"/><item id="nav" href="nav.xhtml" media-type="application/xhtml+xml" properties="nav"/></manifest><spine><itemref idref="one"/><itemref idref="two"/></spine></package>')
    z.writestr("nav.xhtml", '<html xmlns:epub="http://www.idpf.org/2007/ops"><body><nav epub:type="toc"><a href="one.xhtml">TEST FIXTURE: First chapter</a><a href="two.xhtml">TEST FIXTURE: Second chapter</a></nav></body></html>')
    z.writestr("one.xhtml", '<html><body><h1>First chapter</h1><p>This is the first actual chapter in a test fixture. It discusses consciousness, philosophy, myths and software architecture as test vocabulary.</p></body></html>')
    z.writestr("two.xhtml", '<html><body><h1>Second chapter</h1><p>Second chapter text remains private. TEST FIXTURE with no claims about real publications.</p></body></html>')

stream = b"BT /F1 12 Tf 40 700 Td (TEST FIXTURE: PDF page with actual readable text.) Tj ET"
objects = [b"<< /Type /Catalog /Pages 2 0 R >>", b"<< /Type /Pages /Kids [3 0 R] /Count 1 >>", b"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>", b"<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>", b"<< /Length " + str(len(stream)).encode() + b" >>\nstream\n" + stream + b"\nendstream"]
pdf = bytearray(b"%PDF-1.4\n")
offsets = [0]
for i, obj in enumerate(objects, 1):
    offsets.append(len(pdf))
    pdf += f"{i} 0 obj\n".encode() + obj + b"\nendobj\n"
xref = len(pdf)
pdf += f"xref\n0 {len(objects)+1}\n0000000000 65535 f \n".encode()
for offset in offsets[1:]:
    pdf += f"{offset:010d} 00000 n \n".encode()
pdf += f"trailer\n<< /Size {len(objects)+1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n".encode()
(root / "test-page.pdf").write_bytes(pdf)
