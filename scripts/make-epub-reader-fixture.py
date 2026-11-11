"""Original-format EPUB browser fixture; never production seed content."""
from pathlib import Path
from zipfile import ZipFile, ZipInfo, ZIP_STORED

target = Path(__file__).resolve().parents[1] / "tests/e2e/fixtures/epub-reader.epub"
paragraphs = "".join(f'<p>TEST FIXTURE passage {n}: Following one question can reveal another. These words are demonstration content, not a published source.</p>' for n in range(24))
with ZipFile(target, "w", compression=ZIP_STORED) as z:
    def entry(name, text):
        z.writestr(ZipInfo(name, date_time=(2026, 1, 1, 0, 0, 0)), text.encode())
    entry("mimetype", "application/epub+zip")
    entry("META-INF/container.xml", '<container><rootfiles><rootfile full-path="Book/package.opf"/></rootfiles></container>')
    entry("Book/package.opf", '''<package xmlns="http://www.idpf.org/2007/opf"><metadata xmlns:dc="http://purl.org/dc/elements/1.1/"><dc:title>TEST FIXTURE: An original EPUB</dc:title><dc:creator>Fixture author</dc:creator></metadata><manifest>
      <item id="one" href="chapters/one.xhtml" media-type="application/xhtml+xml"/>
      <item id="two" href="chapters/two.xhtml" media-type="application/xhtml+xml"/>
      <item id="nav" href="nav.xhtml" media-type="application/xhtml+xml" properties="nav"/>
      <item id="css" href="styles/book.css" media-type="text/css"/>
      <item id="image" href="images/illustration.svg" media-type="image/svg+xml"/>
      <item id="js" href="bad.js" media-type="text/javascript"/>
      </manifest><spine><itemref idref="one"/><itemref idref="two"/></spine></package>''')
    entry("Book/nav.xhtml", '''<html xmlns:epub="http://www.idpf.org/2007/ops"><body><nav epub:type="toc"><ol>
      <li><a href="chapters/one.xhtml">Following a curious thought</a></li>
      <li><a href="chapters/two.xhtml#second">A different perspective</a></li>
      <li><a href="chapters/two.xhtml#end">Return to the source</a></li>
      </ol></nav></body></html>''')
    entry("Book/styles/book.css", '''@import url("https://epub-fixture.invalid/track.css");
      body {background:#faf7ef;color:#273a3f;margin:32px;font-size:19px;}
      h1 {font-family:Georgia,serif;font-size:2em;font-weight:normal;line-height:1.15;color:#54392f;}
      .illustration {display:block;width:100%;max-width:520px;margin:24px auto;}
      .blocked {background-image:url(https://epub-fixture.invalid/track.png);}
      a {color:#80442b;}''')
    entry("Book/images/illustration.svg", '''<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 520 170"><rect width="520" height="170" fill="#e8e1d2"/><circle cx="90" cy="85" r="45" fill="#9b4d33"/><path d="M180 130L260 35L330 130Z" fill="#526b59"/><rect x="370" y="40" width="75" height="90" fill="#354d57"/><script>window.epubRan=true</script></svg>''')
    entry("Book/chapters/one.xhtml", f'''<html xmlns="http://www.w3.org/1999/xhtml"><head><title>Original first chapter</title><link rel="stylesheet" href="../styles/book.css"/><script>parent.epubRan=true</script><meta http-equiv="refresh" content="0;url=https://epub-fixture.invalid/redirect"/></head><body onload="parent.epubRan=true">
      <h1>Following a curious thought</h1><p>This supplied EPUB brings its own typography, colors and illustration.</p>
      <img class="illustration" src="../images/illustration.svg" alt="Three geometric shapes from the original book"/>
      <p><a href="two.xhtml#second">Continue to the second chapter</a></p>
      <p class="blocked">TEST FIXTURE. Original formatting stays with the book.</p>
      <img src="https://epub-fixture.invalid/track.gif" alt=""/><iframe src="https://epub-fixture.invalid/frame"/><form action="https://epub-fixture.invalid/post"><input name="secret"/></form>
      {paragraphs}<a href="https://example.com/book-reference">External reference</a>
      </body></html>''')
    entry("Book/chapters/two.xhtml", f'''<html xmlns="http://www.w3.org/1999/xhtml"><head><link rel="stylesheet" href="../styles/book.css"/></head><body>
      <h1 id="second">A different perspective</h1><p>TEST FIXTURE: Second chapter source passage.</p>{paragraphs}
      <h2 id="end">Return to the source</h2><p><a href="one.xhtml">Back to the first chapter</a></p></body></html>''')
    entry("Book/bad.js", "window.epubRan=true")
    entry("unlisted.xhtml", "<html><body>Not in the manifest</body></html>")
