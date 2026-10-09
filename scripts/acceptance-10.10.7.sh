#!/usr/bin/env bash
set -euo pipefail

BASE_URL="$JELLYFIN_URL"
ADMIN_USER="ci-admin"
ADMIN_PASS="ci-admin-password"
TEST_USER="ci-randomizer"
TEST_PASS="ci-randomizer-password"
TMP="/tmp/jfr-acceptance"
mkdir -p "$TMP"

AUTH_HEADER='Authorization: MediaBrowser Client="Jellyfin Randomizer CI", DeviceId="jfr-ci", Device="GitHub Actions", Version="10.10.7"'

wait_for_health() {
  for _ in $(seq 1 90); do
    if curl -fsS "$BASE_URL/health" >/dev/null 2>&1; then
      return 0
    fi
    sleep 2
  done
  return 1
}

authenticate() {
  python3 - "$1" "$2" > "$TMP/auth.json" <<'PY'
import json
import sys
print(json.dumps({"Username": sys.argv[1], "Pw": sys.argv[2]}))
PY
  curl -fsS -X POST \
    -H "$AUTH_HEADER" \
    -H 'Content-Type: application/json' \
    "$BASE_URL/Users/AuthenticateByName" \
    --data-binary @"$TMP/auth.json" \
    | python3 -c 'import json,sys; print(json.load(sys.stdin)["AccessToken"])'
}

randomize() {
  local token="$1"
  local mode="$2"
  local library_id="$3"
  local items_json="$4"
  local watched="$5"
  local avoid_recent="$6"
  local strategy="$7"

  python3 - "$mode" "$library_id" "$items_json" "$watched" "$avoid_recent" "$strategy" > "$TMP/randomize.json" <<'PY'
import json
import sys
mode, library_id, items_json, watched, avoid_recent, strategy = sys.argv[1:]
print(json.dumps({
    "mode": mode,
    "libraryId": library_id or None,
    "itemIds": json.loads(items_json),
    "strategy": strategy,
    "watched": watched,
    "avoidRecent": int(avoid_recent)
}, separators=(",", ":")))
PY

  curl -fsS -X POST \
    -H "X-Emby-Token: $token" \
    -H 'Content-Type: application/json' \
    "$BASE_URL/Randomizer/Randomize" \
    --data-binary @"$TMP/randomize.json"
}

admin_get() {
  curl -fsS -H "X-Emby-Token: $ADMIN_TOKEN" "$BASE_URL$1"
}

admin_post() {
  curl -fsS -X POST \
    -H "X-Emby-Token: $ADMIN_TOKEN" \
    -H 'Content-Type: application/json' \
    "$BASE_URL$1" \
    --data-binary "$2"
}

user_get() {
  curl -fsS -H "X-Emby-Token: $USER_TOKEN" "$BASE_URL$1"
}

user_post_empty() {
  curl -fsS -X POST \
    -H "X-Emby-Token: $USER_TOKEN" \
    "$BASE_URL$1"
}

echo "== Jellyfin Randomizer 10.10.7 acceptance =="
wait_for_health

echo "== Complete first-run setup =="
curl -fsS -X POST "$BASE_URL/Startup/Configuration" \
  -H 'Content-Type: application/json' \
  --data '{"ServerName":"Jellyfin Randomizer CI","UICulture":"en-US","MetadataCountryCode":"US","PreferredMetadataLanguage":"en"}' >/dev/null

curl -fsS "$BASE_URL/Startup/User" >/dev/null

python3 - "$ADMIN_USER" "$ADMIN_PASS" > "$TMP/startup-user.json" <<'PY'
import json
import sys
print(json.dumps({"Name": sys.argv[1], "Password": sys.argv[2]}))
PY

curl -fsS -X POST "$BASE_URL/Startup/User" \
  -H 'Content-Type: application/json' \
  --data-binary @"$TMP/startup-user.json" >/dev/null

curl -fsS -X POST "$BASE_URL/Startup/Complete" \
  -H 'Content-Type: application/json' \
  --data '{}' >/dev/null

echo "== Authenticate admin =="
ADMIN_TOKEN="$(authenticate "$ADMIN_USER" "$ADMIN_PASS")"

echo "== Create test libraries =="
create_library() {
  local name="$1"
  local type="$2"
  local path="$3"
  local encoded_name
  local encoded_path
  encoded_name="$(python3 -c 'import urllib.parse,sys; print(urllib.parse.quote(sys.argv[1]))' "$name")"
  encoded_path="$(python3 -c 'import urllib.parse,sys; print(urllib.parse.quote(sys.argv[1]))' "$path")"
  admin_post "/Library/VirtualFolders?name=$encoded_name&collectionType=$type&paths=$encoded_path&refreshLibrary=true" '{"LibraryOptions":{}}' >/dev/null
}

create_library "Allowed Movies" movies "/media/allowed-movies"
create_library "Blocked Movies" movies "/media/blocked-movies"
create_library "Allowed Shows" tvshows "/media/allowed-shows"

echo "== Wait for fixture scan =="
count=0
for _ in $(seq 1 90); do
  count="$(admin_get '/Items?Recursive=true&IncludeItemTypes=Movie,Series,Episode&Limit=500' | python3 -c 'import json,sys; print(json.load(sys.stdin).get("TotalRecordCount",0))')"
  if [ "$count" -ge 7 ]; then
    break
  fi
  sleep 2
done
[ "$count" -ge 7 ]

echo "== Resolve library IDs =="
LIBS="$(admin_get '/Library/VirtualFolders')"
export LIBS
LIB_IDS="$(python3 - <<'PY'
import json, os
libs = {x["Name"]: x["ItemId"] for x in json.loads(os.environ["LIBS"])}
for name in ("Allowed Movies", "Blocked Movies", "Allowed Shows"):
    assert name in libs, name
    print(libs[name])
PY
)"
ALLOWED_MOVIES_LIB="$(printf '%s\n' "$LIB_IDS" | sed -n '1p')"
BLOCKED_MOVIES_LIB="$(printf '%s\n' "$LIB_IDS" | sed -n '2p')"
ALLOWED_SHOWS_LIB="$(printf '%s\n' "$LIB_IDS" | sed -n '3p')"

echo "== Create restricted user =="
python3 - "$TEST_USER" "$TEST_PASS" > "$TMP/test-user.json" <<'PY'
import json
import sys
print(json.dumps({"Name": sys.argv[1], "Password": sys.argv[2]}))
PY
admin_post "/Users/New" "$(cat "$TMP/test-user.json")" >/dev/null

USERS="$(admin_get '/Users')"
TEST_USER_ID="$(printf '%s' "$USERS" | python3 -c 'import json,sys; print(next(u["Id"] for u in json.load(sys.stdin) if u["Name"]==sys.argv[1]))' "$TEST_USER")"

USER_JSON="$(admin_get "/Users/$TEST_USER_ID")"
export USER_JSON ALLOWED_MOVIES_LIB ALLOWED_SHOWS_LIB
POLICY="$(python3 - <<'PY'
import json, os
user = json.loads(os.environ["USER_JSON"])
policy = user["Policy"]
policy["IsAdministrator"] = False
policy["EnableAllFolders"] = False
policy["EnabledFolders"] = [os.environ["ALLOWED_MOVIES_LIB"], os.environ["ALLOWED_SHOWS_LIB"]]
print(json.dumps(policy, separators=(",", ":")))
PY
)"
admin_post "/Users/$TEST_USER_ID/Policy" "$POLICY" >/dev/null

echo "== Verify persisted user policy =="
PERSISTED_USER="$(admin_get "/Users/$TEST_USER_ID")"
export PERSISTED_USER ALLOWED_MOVIES_LIB ALLOWED_SHOWS_LIB
python3 - <<'PY'
import json, os
p = json.loads(os.environ["PERSISTED_USER"])["Policy"]
expected = {
    os.environ["ALLOWED_MOVIES_LIB"],
    os.environ["ALLOWED_SHOWS_LIB"],
}
actual = set(p["EnabledFolders"])
print("EnableAllFolders:", p["EnableAllFolders"])
print("EnabledFolders:", sorted(actual))
print("ExpectedFolders:", sorted(expected))
assert p["EnableAllFolders"] is False
assert actual == expected, (actual, expected)
PY

echo "== Verify plugin configuration round-trip =="
CONFIG="$(admin_get '/Plugins/4e1a3b62-3d7f-4d8f-a0a9-2f2f3c9d7c41/Configuration')"
export CONFIG
UPDATED_CONFIG="$(python3 - <<'PY'
import json, os
config = json.loads(os.environ["CONFIG"])
config["HistorySize"] = 7
config["AnimationDurationMs"] = 1500
print(json.dumps(config, separators=(",", ":")))
PY
)"
admin_post "/Plugins/4e1a3b62-3d7f-4d8f-a0a9-2f2f3c9d7c41/Configuration" "$UPDATED_CONFIG" >/dev/null
PERSISTED_CONFIG="$(admin_get '/Plugins/4e1a3b62-3d7f-4d8f-a0a9-2f2f3c9d7c41/Configuration')"
export PERSISTED_CONFIG
python3 - <<'PY'
import json, os
config = json.loads(os.environ["PERSISTED_CONFIG"])
history = config.get("HistorySize", config.get("historySize"))
duration = config.get("AnimationDurationMs", config.get("animationDurationMs"))
print("Persisted HistorySize:", history)
print("Persisted AnimationDurationMs:", duration)
assert history == 7, config
assert duration == 1500, config
PY

echo "== Verify plugin libraries as admin =="
ADMIN_LIBS="$(curl -fsS -H "X-Emby-Token: $ADMIN_TOKEN" "$BASE_URL/Randomizer/Libraries")"
export ADMIN_LIBS
python3 - <<'PY'
import json, os
def value(item, key):
    return item.get(key, item.get(key[:1].upper() + key[1:]))
names = {value(x, "name") for x in json.loads(os.environ["ADMIN_LIBS"])}
print("Admin Randomizer libraries:", sorted(names))
assert names == {"Allowed Movies", "Allowed Shows", "Blocked Movies"}, names
PY

echo "== Authenticate restricted user =="
USER_TOKEN="$(authenticate "$TEST_USER" "$TEST_PASS")"

echo "== Verify plugin library visibility =="
VISIBLE="$(user_get '/Randomizer/Libraries')"
export VISIBLE
python3 - <<'PY'
import json, os
def value(item, key):
    return item.get(key, item.get(key[:1].upper() + key[1:]))
names = {value(x, "name") for x in json.loads(os.environ["VISIBLE"])}
print("Restricted Randomizer libraries:", sorted(names))
assert names == {"Allowed Movies", "Allowed Shows"}, names
PY

echo "== Verify search pagination =="
SEARCH="$(user_get '/Randomizer/Search?itemType=Movie&limit=1')"
export SEARCH
python3 - <<'PY'
import json, os
items = json.loads(os.environ["SEARCH"])
assert len(items) == 1, items
name = items[0].get("name", items[0].get("Name"))
print("Search result:", name)
assert name == "Allowed Movie 1", items
PY

echo "== Verify search hard cap =="
SEARCH_CAPPED="$(user_get '/Randomizer/Search?itemType=Movie&limit=1000')"
export SEARCH_CAPPED
python3 - <<'PY'
import json, os
items = json.loads(os.environ["SEARCH_CAPPED"])
print("Search limit=1000 returned:", len(items), "items")
assert len(items) <= 100, items
PY


echo "== Resolve fixture IDs =="
ALL_ITEMS="$(admin_get '/Items?Recursive=true&IncludeItemTypes=Movie,Series,Episode&Limit=500')"
USER_ITEMS="$(user_get "/Users/$TEST_USER_ID/Items?Recursive=true&IncludeItemTypes=Movie,Series,Episode&Limit=500")"
export ALL_ITEMS USER_ITEMS
FIXTURES="$(python3 - <<'PY'
import json, os
all_items = json.loads(os.environ["ALL_ITEMS"])["Items"]
user_items = json.loads(os.environ["USER_ITEMS"])["Items"]

def find(items, name, kind):
    return next(i["Id"] for i in items if i.get("Name") == name and i.get("Type") == kind)

for value in (
    find(user_items, "Allowed Movie 1", "Movie"),
    find(user_items, "Test Show A", "Series"),
    find(user_items, "Test Show B", "Series"),
    find(all_items, "Blocked Movie 1", "Movie"),
):
    print(value)
PY
)"
ALLOWED_MOVIE_ID="$(printf '%s\n' "$FIXTURES" | sed -n '1p')"
SHOW_A_ID="$(printf '%s\n' "$FIXTURES" | sed -n '2p')"
SHOW_B_ID="$(printf '%s\n' "$FIXTURES" | sed -n '3p')"
BLOCKED_MOVIE_ID="$(printf '%s\n' "$FIXTURES" | sed -n '4p')"

echo "== Random movie =="
MOVIE_RESULT="$(randomize "$USER_TOKEN" RandomMovie "$ALLOWED_MOVIES_LIB" '["'"$ALLOWED_MOVIE_ID"'"]' All 0 EqualEpisode)"
export MOVIE_RESULT
python3 - <<'PY'
import json, os
r = json.loads(os.environ["MOVIE_RESULT"])
assert r["itemType"] == "Movie", r
assert r["name"] == "Allowed Movie 1", r
PY

echo "== Random show =="
SHOW_RESULT="$(randomize "$USER_TOKEN" RandomShow "$ALLOWED_SHOWS_LIB" '["'"$SHOW_A_ID"'","'"$SHOW_B_ID"'"]' All 0 EqualEpisode)"
export SHOW_RESULT SHOW_A_ID SHOW_B_ID
python3 - <<'PY'
import json, os
r = json.loads(os.environ["SHOW_RESULT"])
assert r["itemType"] == "Series", r
assert r["itemId"] in {os.environ["SHOW_A_ID"], os.environ["SHOW_B_ID"]}, r
PY

echo "== Single-show random episode =="
EPISODE_RESULT="$(randomize "$USER_TOKEN" RandomEpisode "$ALLOWED_SHOWS_LIB" '["'"$SHOW_A_ID"'"]' All 0 EqualEpisode)"
export EPISODE_RESULT
python3 - <<'PY'
import json, os
r = json.loads(os.environ["EPISODE_RESULT"])
assert r["itemType"] == "Episode", r
assert r.get("seriesName") == "Test Show A", r
assert r.get("seasonNumber") == 1, r
assert r.get("episodeNumber") in {1, 2}, r
PY

echo "== Multi-show EqualShow episode =="
MULTI="$(randomize "$USER_TOKEN" RandomEpisode "$ALLOWED_SHOWS_LIB" '["'"$SHOW_A_ID"'","'"$SHOW_B_ID"'"]' All 0 EqualShow)"
export MULTI
python3 - <<'PY'
import json, os
r = json.loads(os.environ["MULTI"])
assert r["itemType"] == "Episode", r
assert r.get("seriesName") in {"Test Show A", "Test Show B"}, r
PY

echo "== Whole TV library EqualEpisode =="
ALL_TV="$(randomize "$USER_TOKEN" RandomEpisode "$ALLOWED_SHOWS_LIB" '[]' All 0 EqualEpisode)"
export ALL_TV
python3 - <<'PY'
import json, os
r = json.loads(os.environ["ALL_TV"])
assert r["itemType"] == "Episode", r
assert r.get("seriesName") in {"Test Show A", "Test Show B"}, r
PY

echo "== Watched filter =="
user_post_empty "/Users/$TEST_USER_ID/PlayedItems/$ALLOWED_MOVIE_ID" >/dev/null
WATCHED="$(randomize "$USER_TOKEN" RandomMovie "$ALLOWED_MOVIES_LIB" '["'"$ALLOWED_MOVIE_ID"'"]' Watched 0 EqualEpisode)"
export WATCHED ALLOWED_MOVIE_ID
python3 - <<'PY'
import json, os
assert json.loads(os.environ["WATCHED"])["itemId"] == os.environ["ALLOWED_MOVIE_ID"]
PY

echo "== Unwatched filter =="
set +e
UNWATCHED_STATUS="$(curl -sS -o "$TMP/unwatched.out" -w '%{http_code}' -X POST \
  -H "X-Emby-Token: $USER_TOKEN" \
  -H 'Content-Type: application/json' \
  "$BASE_URL/Randomizer/Randomize" \
  --data "$(python3 - "$ALLOWED_MOVIES_LIB" "$ALLOWED_MOVIE_ID" <<'PY'
import json, sys
print(json.dumps({
    "mode": "RandomMovie",
    "libraryId": sys.argv[1],
    "itemIds": [sys.argv[2]],
    "strategy": "EqualEpisode",
    "watched": "Unwatched",
    "avoidRecent": 0
}))
PY
)")"
set -e
if [ "$UNWATCHED_STATUS" -eq 200 ]; then
  echo "Watched item incorrectly matched unwatched filter"
  cat "$TMP/unwatched.out"
  exit 1
fi

echo "== Unwatched filter across accessible library =="
UNWATCHED_ALL="$(randomize "$USER_TOKEN" RandomMovie "$ALLOWED_MOVIES_LIB" '[]' Unwatched 0 EqualEpisode)"
export UNWATCHED_ALL ALLOWED_MOVIE_ID
python3 - <<'PY'
import json, os
result = json.loads(os.environ["UNWATCHED_ALL"])
print("Accessible-library unwatched result:", result)
assert result["itemType"] == "Movie", result
assert result["itemId"] != os.environ["ALLOWED_MOVIE_ID"], result
PY

echo "== Empty/no-eligible result =="
set +e
EMPTY_STATUS="$(curl -sS -o "$TMP/empty.out" -w '%{http_code}' -X POST \
  -H "X-Emby-Token: $USER_TOKEN" \
  -H 'Content-Type: application/json' \
  "$BASE_URL/Randomizer/Randomize" \
  --data "$(python3 - "$ALLOWED_MOVIES_LIB" "$ALLOWED_MOVIE_ID" <<'PY'
import json, sys
print(json.dumps({
    "mode": "RandomMovie",
    "libraryId": sys.argv[1],
    "itemIds": [sys.argv[2]],
    "strategy": "EqualEpisode",
    "watched": "Unwatched",
    "avoidRecent": 0
}))
PY
)")"
set -e
test "$EMPTY_STATUS" -eq 404
echo "Empty/no-eligible status: $EMPTY_STATUS"


echo "== History avoidance =="
FIRST="$(randomize "$USER_TOKEN" RandomMovie "$ALLOWED_MOVIES_LIB" '[]' All 1 EqualEpisode)"
SECOND="$(randomize "$USER_TOKEN" RandomMovie "$ALLOWED_MOVIES_LIB" '[]' All 1 EqualEpisode)"
export FIRST SECOND
python3 - <<'PY'
import json, os
assert json.loads(os.environ["FIRST"])["itemId"] != json.loads(os.environ["SECOND"])["itemId"]
PY

echo "== Inaccessible explicit ID =="
set +e
BLOCKED_STATUS="$(curl -sS -o "$TMP/blocked.out" -w '%{http_code}' -X POST \
  -H "X-Emby-Token: $USER_TOKEN" \
  -H 'Content-Type: application/json' \
  "$BASE_URL/Randomizer/Randomize" \
  --data "$(python3 - "$BLOCKED_MOVIES_LIB" "$BLOCKED_MOVIE_ID" <<'PY'
import json, sys
print(json.dumps({
    "mode": "RandomMovie",
    "libraryId": sys.argv[1],
    "itemIds": [sys.argv[2]],
    "strategy": "EqualEpisode",
    "watched": "All",
    "avoidRecent": 0
}))
PY
)")"
set -e
if [ "$BLOCKED_STATUS" -eq 200 ]; then
  echo "Blocked item was returned"
  cat "$TMP/blocked.out"
  exit 1
fi

echo "== Inaccessible explicit ID without library ID =="
set +e
BLOCKED_NO_LIBRARY_STATUS="$(curl -sS -o "$TMP/blocked-no-library.out" -w '%{http_code}' -X POST   -H "X-Emby-Token: $USER_TOKEN"   -H 'Content-Type: application/json'   "$BASE_URL/Randomizer/Randomize"   --data "$(python3 - "$BLOCKED_MOVIE_ID" <<'PY'
import json, sys
print(json.dumps({
    "mode": "RandomMovie",
    "libraryId": None,
    "itemIds": [sys.argv[1]],
    "strategy": "EqualEpisode",
    "watched": "All",
    "avoidRecent": 0
}))
PY
)")"
set -e
if [ "$BLOCKED_NO_LIBRARY_STATUS" -eq 200 ]; then
  echo "Blocked item was returned without a library scope"
  cat "$TMP/blocked-no-library.out"
  exit 1
fi

echo "== Inaccessible library scope cannot be selected =="
set +e
INACCESSIBLE_LIBRARY_STATUS="$(curl -sS -o "$TMP/inaccessible-library.out" -w '%{http_code}' -X POST \
  -H "X-Emby-Token: $USER_TOKEN" \
  -H 'Content-Type: application/json' \
  "$BASE_URL/Randomizer/Randomize" \
  --data "$(python3 - "$BLOCKED_MOVIES_LIB" "$ALLOWED_MOVIE_ID" <<'PY'
import json, sys
print(json.dumps({
    "mode": "RandomMovie",
    "libraryId": sys.argv[1],
    "itemIds": [sys.argv[2]],
    "strategy": "EqualEpisode",
    "watched": "All",
    "avoidRecent": 0
}))
PY
)")"
set -e
test "$INACCESSIBLE_LIBRARY_STATUS" -eq 404
echo "Inaccessible library scope status: $INACCESSIBLE_LIBRARY_STATUS"


echo "== Normal Jellyfin details route =="
user_get "/Users/$TEST_USER_ID/Items/$ALLOWED_MOVIE_ID" >/dev/null

echo "== Jellyfin plugin-manager Disable/Enable regression check =="
PLUGIN_ID="4e1a3b62-3d7f-4d8f-a0a9-2f2f3c9d7c41"
INSTALLED_PLUGINS="$(admin_get '/Plugins')"
export INSTALLED_PLUGINS PLUGIN_ID VERSION
PLUGIN_VERSION="$(python3 - <<'PY'
import json, os
plugins = json.loads(os.environ["INSTALLED_PLUGINS"])
print("Installed plugins:", [(p.get("Name"), p.get("Id"), p.get("Version")) for p in plugins])
plugin_id = os.environ["PLUGIN_ID"].lower()
plugin = next(p for p in plugins if str(p.get("Name", "")).lower() == "jellyfin randomizer")
actual_id = str(plugin.get("Id", "")).lower()
assert actual_id == plugin_id, (actual_id, plugin_id, plugin)
version = plugin.get("Version")
assert version == os.environ["VERSION"], (version, os.environ["VERSION"], plugin)
print(version)
PY
)"
echo "Jellyfin reports Randomizer version: $PLUGIN_VERSION"

disable_status="$(curl -sS -o "$TMP/plugin-disable.out" -w '%{http_code}' -X POST \
  -H "X-Emby-Token: $ADMIN_TOKEN" \
  "$BASE_URL/Plugins/$PLUGIN_ID/$PLUGIN_VERSION/Disable")"
echo "Plugin-manager Disable status: $disable_status"
test "$disable_status" -eq 204

enable_status="$(curl -sS -o "$TMP/plugin-enable.out" -w '%{http_code}' -X POST \
  -H "X-Emby-Token: $ADMIN_TOKEN" \
  "$BASE_URL/Plugins/$PLUGIN_ID/$PLUGIN_VERSION/Enable")"
echo "Plugin-manager Enable status: $enable_status"
test "$enable_status" -eq 204

echo "== Disable Randomizer and verify complete shutdown =="
CURRENT_CONFIG="$(admin_get '/Plugins/4e1a3b62-3d7f-4d8f-a0a9-2f2f3c9d7c41/Configuration')"
DISABLED_CONFIG="$(python3 - "$CURRENT_CONFIG" <<'PY'
import json
import sys
config = json.loads(sys.argv[1])
config["Enabled"] = False
print(json.dumps(config, separators=(",", ":")))
PY
)"
admin_post "/Plugins/4e1a3b62-3d7f-4d8f-a0a9-2f2f3c9d7c41/Configuration" "$DISABLED_CONFIG" >/dev/null

DISABLED_CONFIG_READBACK="$(admin_get '/Plugins/4e1a3b62-3d7f-4d8f-a0a9-2f2f3c9d7c41/Configuration')"
export DISABLED_CONFIG_READBACK
python3 - <<'PY'
import json, os
config = json.loads(os.environ["DISABLED_CONFIG_READBACK"])
value = config.get("Enabled", config.get("enabled"))
print("Randomizer enabled after disable:", value)
assert value is False, config
PY

for endpoint in   "/Randomizer/Page"   "/Randomizer/Script.js"   "/Randomizer/Styles.css"   "/Randomizer/Libraries"   "/Randomizer/Search?itemType=Movie&limit=1"; do
  status="$(curl -sS -o "$TMP/disabled-endpoint.out" -w '%{http_code}' -H "X-Emby-Token: $USER_TOKEN" "$BASE_URL$endpoint")"
  echo "Disabled $endpoint status: $status"
  test "$status" -eq 404
done

DISABLED_WEB="$(curl -fsS -H "X-Emby-Token: $USER_TOKEN" "$BASE_URL/web/index.html")"
if printf '%s' "$DISABLED_WEB" | grep -Fq 'data-jellyfin-randomizer-loader'; then
  echo "Randomizer Web loader remained while disabled."
  exit 1
fi

echo "== Re-enable Randomizer and verify restoration =="
REENABLED_CONFIG="$(python3 - "$DISABLED_CONFIG_READBACK" <<'PY'
import json
import sys
config = json.loads(sys.argv[1])
config["Enabled"] = True
print(json.dumps(config, separators=(",", ":")))
PY
)"
admin_post "/Plugins/4e1a3b62-3d7f-4d8f-a0a9-2f2f3c9d7c41/Configuration" "$REENABLED_CONFIG" >/dev/null

REENABLED_PAGE_STATUS="$(curl -sS -o "$TMP/reenabled-page.out" -w '%{http_code}' -H "X-Emby-Token: $USER_TOKEN" "$BASE_URL/Randomizer/Page")"
test "$REENABLED_PAGE_STATUS" -eq 200

REENABLED_LIBS_STATUS="$(curl -sS -o "$TMP/reenabled-libs.out" -w '%{http_code}' -H "X-Emby-Token: $USER_TOKEN" "$BASE_URL/Randomizer/Libraries")"
test "$REENABLED_LIBS_STATUS" -eq 200

REENABLED_WEB="$(curl -fsS -H "X-Emby-Token: $USER_TOKEN" "$BASE_URL/web/index.html")"
loader_count="$(printf '%s' "$REENABLED_WEB" | grep -o 'data-jellyfin-randomizer-loader' | wc -l)"
echo "Re-enabled Randomizer loader tag count: $loader_count"
test "$loader_count" -eq 2

echo "== Acceptance passed =="