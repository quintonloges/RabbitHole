import { cpSync, mkdirSync, readFileSync } from "node:fs";
import { createRequire } from "node:module";
import path from "node:path";

const require = createRequire(import.meta.url);
const source = path.dirname(require.resolve("pdfjs-dist/package.json"));
const { version } = JSON.parse(readFileSync(path.join(source, "package.json"), "utf8"));
const destination = new URL(`../public/pdfjs/${version}/`, import.meta.url);
mkdirSync(destination, { recursive: true });
// Keep fonts, character maps and image decoders on our origin, including offline installs.
for (const directory of ["cmaps", "standard_fonts", "wasm"]) {
  cpSync(path.join(source, directory), new URL(`${directory}/`, destination), { recursive: true });
}
