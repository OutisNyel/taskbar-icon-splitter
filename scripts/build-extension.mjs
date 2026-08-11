import { build } from "esbuild";
import { cp, mkdir, readFile, rm, writeFile } from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const source = path.join(root, "extension");
const storeBuild = process.argv.includes("--store");
const output = path.join(
  root,
  "dist",
  storeBuild ? "store-extension" : "extension"
);

await rm(output, { recursive: true, force: true });
await mkdir(output, { recursive: true });

await build({
  entryPoints: {
    background: path.join(source, "src", "background.ts"),
    popup: path.join(source, "src", "popup.ts"),
    bootstrap: path.join(source, "src", "bootstrap.ts"),
    welcome: path.join(source, "src", "welcome.ts")
  },
  outdir: output,
  bundle: true,
  format: "esm",
  platform: "browser",
  target: "chrome120",
  sourcemap: !storeBuild,
  minify: false,
  legalComments: "none"
});

for (const file of [
  "popup.html",
  "popup.css",
  "bootstrap.html",
  "welcome.html",
  "welcome.css"
]) {
  await cp(path.join(source, file), path.join(output, file));
}
await cp(path.join(source, "icons"), path.join(output, "icons"), {
  recursive: true
});

const manifest = JSON.parse(
  await readFile(path.join(source, "manifest.json"), "utf8")
);
if (storeBuild) {
  delete manifest.key;
}
await writeFile(
  path.join(output, "manifest.json"),
  `${JSON.stringify(manifest, null, 2)}\n`,
  "utf8"
);

console.log(`${storeBuild ? "Store" : "Edge"} extension built at ${output}`);
