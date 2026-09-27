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
// $ is a stub. ZkPost needs two things from it - a selector that finds the token input, and
// $.post - so the test drives both by hand rather than pretending to be a browser.
let tokenOnPage = null;
const posted = [];
// ZkPrompt is given a form object and looks inside it, so the stub carries the fields it would
// find and records what gets written to them.
global.jQuery = global.$ = function (selector) {
    if (selector === 'input[name="__RequestVerificationToken"]') {
        return { first: () => ({ val: () => tokenOnPage }) };
    }
    if (selector && selector.fields) {
        return { find: (what) => ({ val: (v) => { selector.fields[what] = v; } }) };
    }
    return { ready: () => {} };
};
global.jQuery.fn = {};
global.jQuery.extend = Object.assign;
global.jQuery.post = (url, data, done) => { posted.push({ url, data, done }); return "sent"; };
global.document = { addEventListener: () => {} };

const file = path.join(__dirname, "..", "Zero-K.info", "Scripts", "site_main.js");
const { BuildHistoryUrl, ZkPost, ZkPrompt } = require(file);

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

// ZkPost. The rating stars used $.get, so a crafted link set somebody else's rating; the token
// riding along is the whole point, and forgetting to attach it would leave a POST that 400s.
tokenOnPage = "TOKEN-ABC";
posted.length = 0;
const sent = ZkPost("/Maps/Rate?id=7", { rating: 4 });
check(posted.length, 1, "ZkPost sends one request");
check(posted[0].url, "/Maps/Rate?id=7", "to the url it was given");
check(posted[0].data.rating, 4, "with the caller's data");
check(posted[0].data.__RequestVerificationToken, "TOKEN-ABC", "and the page's anti-forgery token");
check(sent, "sent", "returning what $.post returned");

// The caller's object is not modified: Detail.cshtml builds one per star click, but a caller
// reusing an object would otherwise find a stale token welded into it.
const original = { rating: 4 };
ZkPost("/Maps/Rate", original);
check(Object.prototype.hasOwnProperty.call(original, "__RequestVerificationToken"), false,
      "without writing the token into the caller's object");

// No token on the page means the POST would 400. Saying nothing was sent is more use than that.
tokenOnPage = undefined;
posted.length = 0;
check(ZkPost("/Maps/Rate", { rating: 4 }), null, "ZkPost returns null when the page has no token");
check(posted.length, 0, "and sends nothing rather than a request that would be refused");

// ZkPrompt. It replaced an onclick that pasted the answer into the link's own href, which sent
// the poll by GET, mangled a slogan containing & or #, and - if the prompt was cancelled - went
// ahead anyway and created the poll with no text.
let asked = null;
global.prompt = (question, suggestion) => { asked = { question, suggestion }; return promptAnswer; };
let promptAnswer = "vote me!";

let form = { fields: {} };
check(ZkPrompt(form, "text", "What is your slogan?", "vote me!"), true, "ZkPrompt allows the submit");
check(asked.question, "What is your slogan?", "after asking the question it was given");
check(asked.suggestion, "vote me!", "with the suggested answer");
check(form.fields['input[name="text"]'], "vote me!", "and putting the answer in the named field");

// The old code built a URL, so these characters ended or corrupted it. In a form field they are
// just characters, which is the point of moving off the href.
promptAnswer = "me & my #1 friend";
form = { fields: {} };
ZkPrompt(form, "text", "q", "s");
check(form.fields['input[name="text"]'], "me & my #1 friend", "an answer with & and # survives intact");

// Cancelling. The bug this replaces created the poll anyway, with no text.
promptAnswer = null;
form = { fields: {} };
check(ZkPrompt(form, "text", "q", "s"), false, "a cancelled prompt stops the submit");
check(Object.keys(form.fields).length, 0, "and writes nothing into the form");

console.log();
console.log(failures === 0 ? "all checks passed" : failures + " check(s) failed");
process.exit(failures === 0 ? 0 : 1);
