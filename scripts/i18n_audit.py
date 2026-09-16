# Every literal handed to t() that no dictionary can answer.
#
# t() falls back to the source string, so a missing key shows Vietnamese with no
# error in the console, no failing test and nothing in the build output. That is
# why four separate rounds of "chỗ này chưa dịch" were found by eye instead. This
# finds them all at once.
#
#   python scripts/i18n_audit.py          # exits 1 if anything is missing
#
# It only sees literals. t(server.value) — amenity labels, statuses, help groups —
# cannot be checked statically; for those, load a page with the interface set to a
# non-Vietnamese language and look for text still carrying Vietnamese diacritics.
import io, os, re, sys

# A Windows console runs cp1258 — the Vietnamese code page, and it spells
# Vietnamese with combining marks, so it cannot encode the precomposed letters
# this script is about to print. Without it the audit dies on the first missing
# key it finds — that is, exactly when it has something to say.
import sys
if hasattr(sys.stdout, 'reconfigure'):
    sys.stdout.reconfigure(encoding='utf-8')


SRC = os.path.join('src', 'StayHost.Web', 'ClientApp', 'src')
I18N = os.path.join(SRC, 'lib', 'i18n')

KEY = re.compile(r"^\s*('((?:[^'\\]|\\.)*)'|\"((?:[^\"\\]|\\.)*)\")\s*:", re.M)
CALL = re.compile(r"\bt\(\s*('((?:[^'\\]|\\.)*)'|\"((?:[^\"\\]|\\.)*)\")")
NUMBERS = re.compile(r'\d[\d.,]*')

# toast('…') — the one call that translates whatever it is handed (store.js), so
# a literal written there is as much UI text as one inside t().
TOAST = re.compile(r"""toast\(\s*(['"])(.*?)\1\s*\)""")
VIETNAMESE = re.compile('[À-ỹ]')

# The same in every language, so no entry is wanted or missing.
IGNORED = {'…'}


def unquote(match, a, b):
    raw = match.group(a) if match.group(a) is not None else match.group(b)
    return raw.replace("\\'", "'").replace('\\"', '"')


def main():
    known = set()
    for path in (os.path.join(SRC, 'lib', 'i18n.js'), os.path.join(I18N, 'pages-en.js')):
        for m in KEY.finditer(io.open(path, encoding='utf-8').read()):
            known.add(unquote(m, 2, 3))

    used = {}
    for root, dirs, files in os.walk(SRC):
        dirs[:] = [d for d in dirs if d != 'i18n']
        for name in files:
            if not name.endswith(('.jsx', '.js')):
                continue
            path = os.path.join(root, name)
            rel = os.path.relpath(path, SRC).replace(os.sep, '/')
            for m in CALL.finditer(io.open(path, encoding='utf-8').read()):
                used.setdefault(unquote(m, 2, 3), set()).add(rel)

    missing = {}
    for lit, where in used.items():
        if not lit.strip() or lit in known or lit in IGNORED:
            continue
        # A string that is one shape away from a known key is already answered:
        # t() normalises every run of digits to {} before its second lookup.
        if NUMBERS.sub('{}', lit) in known:
            continue
        missing[lit] = sorted(where)

    # Text handed to toast() rather than to t(). toast() translates what it is
    # given (store.js), so a literal written at a call site is UI text like any
    # other — it just needs an entry before a reader of another language sees
    # anything but Vietnamese. Counted, not failed: the wiring landed first and
    # the entries are their own piece of work, and a debt nobody measures is a
    # debt nobody pays.
    toasted = {}
    for root, dirs, files in os.walk(SRC):
        dirs[:] = [d for d in dirs if d != 'i18n']
        for name in files:
            if not name.endswith(('.jsx', '.js')):
                continue
            path = os.path.join(root, name)
            rel = os.path.relpath(path, SRC).replace(os.sep, '/')
            for m in TOAST.finditer(io.open(path, encoding='utf-8').read()):
                lit = m.group(2)
                if len(lit) < 4 or not VIETNAMESE.search(lit):
                    continue
                if lit in known or NUMBERS.sub('{}', lit) in known:
                    continue
                toasted.setdefault(lit, set()).add(rel)

    # Every language must answer the same set, or switching language turns some of
    # the page back to Vietnamese.
    counts = {}
    for name in sorted(os.listdir(I18N)):
        if name.startswith('pages-'):
            text = io.open(os.path.join(I18N, name), encoding='utf-8').read()
            counts[name] = len(KEY.findall(text))

    print('t() literals used : %d' % len(used))
    print('dictionary keys   : %s' % ', '.join('%s=%d' % (n[6:-3], c) for n, c in counts.items()))
    print('missing keys      : %d' % len(missing))
    print('chu qua toast() chua co khoa : %d' % len(toasted))

    for lit in sorted(missing):
        print('  %-70s | %s' % (lit[:70], ', '.join(missing[lit])[:48]))

    uneven = len(set(counts.values())) > 1
    if uneven:
        print('\nCác từ điển không bằng nhau — ngôn ngữ thiếu khoá sẽ hiện tiếng Việt.')

    return 1 if (missing or uneven) else 0


if __name__ == '__main__':
    sys.exit(main())
