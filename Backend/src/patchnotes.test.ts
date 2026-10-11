import { test } from "node:test";
import assert from "node:assert/strict";
import { htmlToTree, patchnoteList, putPatchnote, resetPatchnotes } from "./patchnotes.js";

test("NC's editor HTML becomes a small safe tree", () => {
  const html = '<div data-contents-type="image"><img src="https://x.playnccdn.com/a.jpg" alt="b"></div><div data-contents-type="text"><strong>Hello, this is AION 2.</strong></div>'
    + '<div data-contents-type="text">​</div><ul><li><div data-contents-type="text">Drop rate of &quot;Raw Leather&quot; &amp; more</div><ul><li><div>Nested</div></li></ul></li></ul>'
    + '<script>alert(1)</script><div onclick="x()"><a href="javascript:alert(1)">bad</a> <a href="https://example.com/x">ok</a></div><style>p{}</style>';
  assert.deepEqual(htmlToTree(html), [
    { tag: "p", children: [{ tag: "strong", children: ["Hello, this is AION 2."] }] },
    { tag: "ul", children: [{ tag: "li", children: [{ tag: "p", children: ['Drop rate of "Raw Leather" & more'] }, { tag: "ul", children: [{ tag: "li", children: [{ tag: "p", children: ["Nested"] }] }] }] }] },
    { tag: "p", children: ["bad", " ", { tag: "a", href: "https://example.com/x", children: ["ok"] }] },
  ]);
});

test("nothing outside the allowed tags survives", () => {
  const json = JSON.stringify(htmlToTree('<div><img src=x onerror=alert(1)><iframe src="https://evil"></iframe><span style="x">text</span><a href="http://plain.example">p</a></div>'));
  assert.ok(!/img|iframe|onerror|http:/.test(json), json);
  assert.ok(json.includes("text"));
});

test("the list is per language, newest first, with the link to NC", () => {
  resetPatchnotes();
  putPatchnote({ id: "a".repeat(24), lang: "en", contentId: 1, title: "Patch Notes | Oct. 6", posted: 100, updated: 100, body: [] });
  putPatchnote({ id: "b".repeat(24), lang: "en", contentId: 2, title: "Patch Notes | Oct. 13", posted: 200, updated: 200, body: [] });
  putPatchnote({ id: "c".repeat(24), lang: "de", contentId: 3, title: "Patch-Notizen", posted: 200, updated: 200, body: [] });
  const en = patchnoteList("ru");
  assert.equal(en.lang, "en");
  assert.deepEqual(en.items.map((x) => x.title), ["Patch Notes | Oct. 13", "Patch Notes | Oct. 6"]);
  assert.equal(patchnoteList("de").items[0].url, `https://aion2.plaync.com/de-de/board/update/view?articleId=${"c".repeat(24)}`);
  resetPatchnotes();
});
