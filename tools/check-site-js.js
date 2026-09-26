// The parts of Zero-K.info/Scripts/site_main.js that are arithmetic rather than browser.
//
//     node tools/check-site-js.js
//
// The first JavaScript test in this repository, and it exists for a specific reason:
// BuildHistoryUrl replaced jQuery.param.querystring from a 44 KB plugin, and nothing else here
// would notice if it got the merge wrong. The site's own pages render either way - the address
// bar would just quietly stop matching the form.
//
// It loads the real file rather than a copy. site_main.js is browser code, so the few globals it
// touches at load time are stubbed; nothing in BuildHistoryUrl uses them.
const path = require("path");
const Module = require("module");

global.window = { location: { toString: () => "https://zero-k.info/" }, history: {} };
global.jQuery = global.$ = function () { return { ready: () => {} }; };
global.jQuery.fn = {};
global.document = { addEventListener: () => {} };

const file = path.join(__dirname, "..", "Zero-K.info", "Scripts", "site_main.js");
const { BuildHistoryUrl } = require(file);

let failures = 0;
function check(actual, expected, what) {
    if (actual === expected) {
        console.log("   ok    " + what);
    } else {
        console.log("   FAIL  " + what + "\n           expected: " + expected + "\n           actual:   " + actual);
        failures++;
    }
}

console.log("site_main.js:");

check(BuildHistoryUrl("https://zero-k.info/Battles", "tab=2"),
      "https://zero-k.info/Battles?tab=2",
      "adds a parameter to a URL that has none");

check(BuildHistoryUrl("https://zero-k.info/Battles?tab=1", "tab=2"),
      "https://zero-k.info/Battles?tab=2",
      "replaces a parameter rather than appending a second copy");

check(BuildHistoryUrl("https://zero-k.info/Battles?page=3", "tab=2"),
      "https://zero-k.info/Battles?page=3&tab=2",
      "leaves parameters the caller did not mention");

// What frm.serialize() produces for a multi-select, and the reason the old code had to strip
// the "[]" that bbq added back in.
check(BuildHistoryUrl("https://zero-k.info/Battles?map=a", "map=b&map=c"),
      "https://zero-k.info/Battles?map=b&map=c",
      "a repeated key replaces the old value wholesale, with no [] appended");

check(BuildHistoryUrl("https://zero-k.info/Battles#anchor", "tab=2"),
      "https://zero-k.info/Battles?tab=2",
      "drops the fragment, as the old # hack did");

check(BuildHistoryUrl("https://zero-k.info/Battles", "?tab=2"),
      "https://zero-k.info/Battles?tab=2",
      "accepts params with a leading ? or #");

check(BuildHistoryUrl("https://zero-k.info/Search", "q=a%20b"),
      "https://zero-k.info/Search?q=a+b",
      "keeps an encoded value encoded");

console.log();
console.log(failures === 0 ? "all checks passed" : failures + " check(s) failed");
process.exit(failures === 0 ? 0 : 1);
