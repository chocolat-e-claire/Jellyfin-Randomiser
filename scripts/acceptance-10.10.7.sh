#!/usr/bin/env bash
set -euo pipefail

BASE_URL="${JELLYFIN_URL:-http://127.0.0.1:8096}"
ADMIN_USER="${JELLYFIN_ADMIN_USER:-ci-admin}"
ADMIN_PASS="${JELLYFIN_ADMIN_PASS:-CiAdmin-10.10.7!}"
TEST_USER="${JELLYFIN_TEST_USER:-ci-randomizer}"
TEST_PASS="${JELLYFIN_TEST_PASS:-CiRandomizer-10.10.7!}"

MEDIA_ROOT="${MEDIA_ROOT:-$PWD/smoke-media}"
TMP="${TMPDIR:-/tmp}/jfr-acceptance"
mkdir -p "$TMP"

AUTH_HEADER='X-Emby-Authorization: MediaBrowser Client="Jellyfin Randomizer CI", Device="GitHub Actions", DeviceId="jfr-ci", Version="0.1"'

api() {
  local method="$1"; shift
  curl -fsS -X "$method" -H "$AUTH_HEADER" "$@"
}

token_auth() {
  local user="$1" pass="$2"
  curl -fsS -X POST \
    -H "$AUTH_HEADER" \
    -H 'Content-Type: application/json' \
    "$BASE_URL/Users/AuthenticateByName" \
    --data "{"Username":"$user","Pw":"$pass"}" \
    | python3 -c 'import json,sys; print(json.load(sys.stdin)["AccessToken"])'
}

auth_api() {
  curl -fsS -H "X-Emby-Token: $USER_TOKEN" "$BASE_URL$1"
}

auth_post() {
  curl -fsS -X POST -H "X-Emby-Token: $USER_TOKEN" -H 'Content-Type: application/json' "$BASE_URL$1" "$@"
}

wait_for_http() {
  local path="$1"
  local attempts="${2:-90}"
  for _ in $(seq 1 "$attempts"); do
    if curl -fsS "$BASE_URL$path" >/dev/null 2>&1; then
      return 0
    fi
    sleep 2
  done
  echo "Timed out waiting for $path" >&2
  return 1
}

echo "== Jellyfin Randomizer 10.10.7 acceptance =="
wait_for_http /health

echo "== Complete first-run setup =="
curl -fsS -X POST "$BASE_URL/Startup/Configuration" \
  -H 'Content-Type: application/x-www-form-urlencoded' \
  --data 'UICulture=en-US&MetadataCountryCode=US&PreferredMetadataLanguage=en' >/dev/null
curl -fsS -X POST "$BASE_URL/Startup/User" \
  -H 'Content-Type: application/x-www-form-urlencoded' \
  --data-urlencode "Name=$ADMIN_USER" \
  --data-urlencode "Password=$ADMIN_PASS" >/dev/null
curl -fsS -X POST "$BASE_URL/Startup/RemoteAccess" \
  -H 'Content-Type: application/x-www-form-urlencoded' \
  --data 'EnableRemoteAccess=false&EnableAutomaticPortMapping=false' >/dev/null
curl -fsS -X POST "$BASE_URL/Startup/Complete" >/dev/null

echo "== Authenticate admin =="
ADMIN_TOKEN="$(token_auth "$ADMIN_USER" "$ADMIN_PASS")"
ADMIN_HEADER="X-Emby-Token: $ADMIN_TOKEN"

admin_get() {
  curl -fsS -H "$ADMIN_HEADER" "$BASE_URL$1"
}

admin_post_json() {
  curl -fsS -X POST -H "$ADMIN_HEADER" -H 'Content-Type: application/json' "$BASE_URL$1" --data "$2"
}

echo "== Create test libraries =="
create_library() {
  local name="$1" collection="$2" path="$3"
  curl -fsS -X POST \
    -H "$ADMIN_HEADER" \
    -H 'Content-Type: application/json' \
    "$BASE_URL/Library/VirtualFolders?name=$(python3 -c 'import urllib.parse,sys; print(urllib.parse.quote(sys.argv[1]))' "$name")&collectionType=$collection&paths=$(python3 -c 'import urllib.parse,sys; print(urllib.parse.quote(sys.argv[1]))' "$path")&refreshLibrary=true" \
    --data '{"LibraryOptions":{}}' >/dev/null
}
create_library "Allowed Movies" movies "/media/allowed-movies"
create_library "Blocked Movies" movies "/media/blocked-movies"
create_library "Allowed Shows" tvshows "/media/allowed-shows"

echo "== Wait for media scan =="
for _ in $(seq 1 90); do
  counts="$(admin_get '/Items?Recursive=true&IncludeItemTypes=Movie,Series,Episode&Limit=500' | python3 -c 'import json,sys; d=json.load(sys.stdin); print(d.get("TotalRecordCount",0))')"
  if [ "$counts" -ge 6 ]; then
    break
  fi
  sleep 2
done
[ "$counts" -ge 6 ]

echo "== Resolve library and fixture item IDs =="
LIBS_JSON="$(admin_get '/Library/VirtualFolders')"
export LIBS_JSON
readarray -t IDS < <(python3 - <<'PY'
import json, os
libs=json.loads(os.environ["LIBS_JSON"])
by={x["Name"]:x["ItemId"] for x in libs}
for name in ("Allowed Movies","Blocked Movies","Allowed Shows"):
    print(by[name])
PY
)
ALLOWED_MOVIES_LIB="${IDS[0]}"
BLOCKED_MOVIES_LIB="${IDS[1]}"
ALLOWED_SHOWS_LIB="${IDS[2]}"

ITEMS_JSON="$(admin_get '/Items?Recursive=true&IncludeItemTypes=Movie,Series,Episode&Limit=500&Fields=MediaSources,Overview')"
export ITEMS_JSON
python3 - <<'PY'
import json, os
items=json.loads(os.environ["ITEMS_JSON"])["Items"]
assert any(i.get("Name")=="Allowed Movie 1" and i.get("Type")=="Movie" for i in items)
assert any(i.get("Name")=="Blocked Movie 1" and i.get("Type")=="Movie" for i in items)
assert any(i.get("Name")=="Test Show A" and i.get("Type")=="Series" for i in items)
assert any(i.get("Name")=="Test Show B" and i.get("Type")=="Series" for i in items)
episodes=[i for i in items if i.get("Type")=="Episode"]
assert len(episodes) >= 4, f"expected >=4 episodes, got {len(episodes)}"
PY

echo "== Create restricted test user =="
curl -fsS -X POST \
  -H "$ADMIN_HEADER" -H 'Content-Type: application/json' \
  "$BASE_URL/Users/New" \
  --data "{"Name":"$TEST_USER","Password":"$TEST_PASS"}" >/dev/null

USERS_JSON="$(admin_get '/Users')"
TEST_USER_ID="$(printf '%s' "$USERS_JSON" | python3 -c 'import json,sys; users=json.load(sys.stdin); print(next(x["Id"] for x in users if x["Name"]==sys.argv[1]))' "$TEST_USER")"

USER_JSON="$(admin_get "/Users/$TEST_USER_ID")"
export USER_JSON ALLOWED_MOVIES_LIB ALLOWED_SHOWS_LIB
POLICY="$(python3 - <<'PY'
import json, os
d=json.loads(os.environ["USER_JSON"])
p=d["Policy"]
p["IsAdministrator"]=False
p["EnableAllFolders"]=False
p["EnabledFolders"]=[os.environ["ALLOWED_MOVIES_LIB"], os.environ["ALLOWED_SHOWS_LIB"]]
print(json.dumps(p,separators=(",",":")))
PY
)"
curl -fsS -X POST \
  -H "$ADMIN_HEADER" -H 'Content-Type: application/json' \
  "$BASE_URL/Users/$TEST_USER_ID/Policy" \
  --data "$POLICY" >/dev/null

echo "== Authenticate restricted user =="
USER_TOKEN="$(token_auth "$TEST_USER" "$TEST_PASS")"

echo "== Verify plugin libraries are permission-filtered =="
LIBRARIES="$(auth_api '/Randomizer/Libraries')"
export LIBRARIES
python3 - <<'PY'
import json, os
names={x["name"] for x in json.loads(os.environ["LIBRARIES"])}
assert names == {"Allowed Movies","Allowed Shows"}, names
PY

echo "== Verify search and bounded pagination =="
MOVIES_ONE="$(curl -fsS -H "X-Emby-Token: $USER_TOKEN" "$BASE_URL/Randomizer/Search?itemType=Movie&limit=1")"
export MOVIES_ONE
python3 - <<'PY'
import json, os
items=json.loads(os.environ["MOVIES_ONE"])
assert len(items) == 1
assert items[0]["name"] == "Allowed Movie 1"
PY

echo "== Resolve permitted and forbidden fixture IDs =="
USER_ITEMS="$(auth_api '/Users/'"$TEST_USER_ID"'/Items?Recursive=true&IncludeItemTypes=Movie,Series,Episode&Limit=500&Fields=Overview')"
export USER_ITEMS ITEMS_JSON
readarray -t FIXTURES < <(python3 - <<'PY'
import json, os
all_items=json.loads(os.environ["ITEMS_JSON"])["Items"]
user_items=json.loads(os.environ["USER_ITEMS"])["Items"]
def find(items,name,typ):
    return next(i["Id"] for i in items if i.get("Name")==name and i.get("Type")==typ)
print(find(user_items,"Allowed Movie 1","Movie"))
print(find(user_items,"Test Show A","Series"))
print(find(user_items,"Test Show B","Series"))
print(find(all_items,"Blocked Movie 1","Movie"))
PY
)
ALLOWED_MOVIE_ID="${FIXTURES[0]}"
SHOW_A_ID="${FIXTURES[1]}"
SHOW_B_ID="${FIXTURES[2]}"
BLOCKED_MOVIE_ID="${FIXTURES[3]}"

echo "== Random movie =="
MOVIE_RESULT="$(curl -fsS -X POST -H "X-Emby-Token: $USER_TOKEN" -H 'Content-Type: application/json' \
  "$BASE_URL/Randomizer/Randomize" \
  --data "{"mode":"RandomMovie","libraryId":"$ALLOWED_MOVIES_LIB","itemIds":["$ALLOWED_MOVIE_ID"],"watched":"All","avoidRecent":0}")"
export MOVIE_RESULT
python3 - <<'PY'
import json,os
r=json.loads(os.environ["MOVIE_RESULT"])
assert r["itemId"] != "", r
assert r["itemType"] == "Movie", r
assert r["name"] == "Allowed Movie 1", r
PY

echo "== Random show =="
SHOW_RESULT="$(curl -fsS -X POST -H "X-Emby-Token: $USER_TOKEN" -H 'Content-Type: application/json' \
  "$BASE_URL/Randomizer/Randomize" \
  --data "{"mode":"RandomShow","libraryId":"$ALLOWED_SHOWS_LIB","itemIds":["$SHOW_A_ID","$SHOW_B_ID"],"watched":"All","avoidRecent":0}")"
export SHOW_RESULT
python3 - <<'PY'
import json,os
r=json.loads(os.environ["SHOW_RESULT"])
assert r["itemType"]=="Series", r
assert r["itemId"] in {os.environ["SHOW_A_ID"], os.environ["SHOW_B_ID"]}, r
PY

echo "== Random episode from one show =="
EPISODE_RESULT="$(curl -fsS -X POST -H "X-Emby-Token: $USER_TOKEN" -H 'Content-Type: application/json' \
  "$BASE_URL/Randomizer/Randomize" \
  --data "{"mode":"RandomEpisode","libraryId":"$ALLOWED_SHOWS_LIB","itemIds":["$SHOW_A_ID"],"strategy":"EqualEpisode","watched":"All","avoidRecent":0}")"
export EPISODE_RESULT
python3 - <<'PY'
import json,os
r=json.loads(os.environ["EPISODE_RESULT"])
assert r["itemType"]=="Episode", r
assert r.get("seriesName")=="Test Show A", r
assert r.get("seasonNumber")==1, r
assert r.get("episodeNumber") in {1,2}, r
PY

echo "== Random episode from multiple shows / EqualShow =="
MULTI_RESULT="$(curl -fsS -X POST -H "X-Emby-Token: $USER_TOKEN" -H 'Content-Type: application/json' \
  "$BASE_URL/Randomizer/Randomize" \
  --data "{"mode":"RandomEpisode","libraryId":"$ALLOWED_SHOWS_LIB","itemIds":["$SHOW_A_ID","$SHOW_B_ID"],"strategy":"EqualShow","watched":"All","avoidRecent":0}")"
export MULTI_RESULT
python3 - <<'PY'
import json,os
r=json.loads(os.environ["MULTI_RESULT"])
assert r["itemType"]=="Episode", r
assert r.get("seriesName") in {"Test Show A","Test Show B"}, r
PY

echo "== Entire accessible TV library / EqualEpisode =="
ALL_TV_RESULT="$(curl -fsS -X POST -H "X-Emby-Token: $USER_TOKEN" -H 'Content-Type: application/json' \
  "$BASE_URL/Randomizer/Randomize" \
  --data "{"mode":"RandomEpisode","libraryId":"$ALLOWED_SHOWS_LIB","itemIds":[],"strategy":"EqualEpisode","watched":"All","avoidRecent":0}")"
export ALL_TV_RESULT
python3 - <<'PY'
import json,os
r=json.loads(os.environ["ALL_TV_RESULT"])
assert r["itemType"]=="Episode", r
assert r.get("seriesName") in {"Test Show A","Test Show B"}, r
PY

echo "== Watched/unwatched filters =="
curl -fsS -X POST -H "X-Emby-Token: $USER_TOKEN" "$BASE_URL/Users/$TEST_USER_ID/PlayedItems/$ALLOWED_MOVIE_ID" >/dev/null
WATCHED_RESULT="$(curl -fsS -X POST -H "X-Emby-Token: $USER_TOKEN" -H 'Content-Type: application/json' \
  "$BASE_URL/Randomizer/Randomize" \
  --data "{"mode":"RandomMovie","libraryId":"$ALLOWED_MOVIES_LIB","itemIds":["$ALLOWED_MOVIE_ID"],"watched":"Watched","avoidRecent":0}")"
export WATCHED_RESULT
python3 - <<'PY'
import json,os
assert json.loads(os.environ["WATCHED_RESULT"])["itemId"]==os.environ["ALLOWED_MOVIE_ID"]
PY

echo "== History avoidance =="
FIRST="$(curl -fsS -X POST -H "X-Emby-Token: $USER_TOKEN" -H 'Content-Type: application/json' \
  "$BASE_URL/Randomizer/Randomize" \
  --data "{"mode":"RandomMovie","libraryId":null,"itemIds":[],"watched":"All","avoidRecent":0}")"
SECOND="$(curl -fsS -X POST -H "X-Emby-Token: $USER_TOKEN" -H 'Content-Type: application/json' \
  "$BASE_URL/Randomizer/Randomize" \
  --data "{"mode":"RandomMovie","libraryId":null,"itemIds":[],"watched":"All","avoidRecent":1}")"
export FIRST SECOND
python3 - <<'PY'
import json,os
a=json.loads(os.environ["FIRST"])["itemId"]
b=json.loads(os.environ["SECOND"])["itemId"]
assert a != b, (a,b)
PY

echo "== Unwatched filter excludes a watched explicit item =="
set +e
STATUS=$(curl -sS -o "$TMP/unwatched.out" -w '%{http_code}' -X POST \
  -H "X-Emby-Token: $USER_TOKEN" -H 'Content-Type: application/json' \
  "$BASE_URL/Randomizer/Randomize" \
  --data "{\"mode\":\"RandomMovie\",\"libraryId\":\"$ALLOWED_MOVIES_LIB\",\"itemIds\":[\"$ALLOWED_MOVIE_ID\"],\"watched\":\"Unwatched\",\"avoidRecent\":0}")
set -e
if [ "$STATUS" -eq 200 ]; then
  echo "FAIL: watched item was returned by the unwatched filter"
  cat "$TMP/unwatched.out"
  exit 1
fi

echo "== Permission enforcement against an inaccessible explicit ID =="
set +e
STATUS=$(curl -sS -o "$TMP/blocked.out" -w '%{http_code}' -X POST \
  -H "X-Emby-Token: $USER_TOKEN" -H 'Content-Type: application/json' \
  "$BASE_URL/Randomizer/Randomize" \
  --data "{"mode":"RandomMovie","libraryId":"$BLOCKED_MOVIES_LIB","itemIds":["$BLOCKED_MOVIE_ID"],"watched":"All","avoidRecent":0}")
set -e
if [ "$STATUS" -eq 200 ]; then
  echo "FAIL: inaccessible content was returned"
  cat "$TMP/blocked.out"
  exit 1
fi

echo "== Normal Jellyfin item details endpoint =="
curl -fsS -H "X-Emby-Token: $USER_TOKEN" "$BASE_URL/Users/$TEST_USER_ID/Items/$ALLOWED_MOVIE_ID" >/dev/null

echo "== Acceptance passed =="
