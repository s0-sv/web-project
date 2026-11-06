#!/usr/bin/env bash

set -u

BASE="${1:-http://localhost:5298}/api"
RUN="$(date +%s)$$"
TMP="$(mktemp -d)"
trap 'rm -rf "$TMP"' EXIT

FAILED=0
BODY=""
TOKEN=""

call() {
    local expected="$1" description="$2"
    shift 2

    local status
    status="$(curl -s -o "$TMP/body" -w '%{http_code}' ${TOKEN:+-H "Authorization: Bearer $TOKEN"} "$@")"
    BODY="$(cat "$TMP/body")"

    local mark="OK"
    if [ "$status" != "$expected" ]; then
        mark="FAILED, expected $expected"
        FAILED=$((FAILED + 1))
    fi

    printf '\n### %s\n' "$description"
    printf '%s -> HTTP %s [%s]\n' "${*: -1}" "$status" "$mark"
    [ -n "$BODY" ] && printf '%s\n' "$BODY" | sed -E 's/"accessToken":"[^"]+"/"accessToken":"***"/' | cut -c1-600
}

post_json() { call "$1" "$2" -X POST -H 'Content-Type: application/json' -d "$3" "$BASE$4"; }
put_json() { call "$1" "$2" -X PUT -H 'Content-Type: application/json' -d "$3" "$BASE$4"; }
new_id() { printf '%s' "$BODY" | grep -oE '"id":[0-9]+' | head -1 | cut -d: -f2; }

echo "=== Authentication ==="
call 401 "Anonymous request is rejected" "$BASE/subjects"
ACCOUNT="{\"email\":\"demo.$RUN@example.com\",\"password\":\"Demo-$RUN-pass\",\"fullName\":\"Demo Owner\",\"workspaceName\":\"Demo $RUN\"}"
post_json 201 "Register an account and a workspace" "$ACCOUNT" /auth/register
post_json 401 "Wrong password is rejected" "{\"email\":\"demo.$RUN@example.com\",\"password\":\"wrong-password-1\"}" /auth/token
post_json 200 "Get an access token" "{\"email\":\"demo.$RUN@example.com\",\"password\":\"Demo-$RUN-pass\"}" /auth/token
TOKEN="$(printf '%s' "$BODY" | grep -oE '"accessToken":"[^"]+"' | cut -d'"' -f4)"
call 200 "Current user" "$BASE/auth/me"

echo
echo "=== Read collections ==="
call 200 "List subjects" "$BASE/subjects"
call 200 "List tutors" "$BASE/tutors"
call 200 "List students" "$BASE/students"
call 200 "List lessons" "$BASE/lessons"
call 200 "List materials" "$BASE/materials"

echo
echo "=== Create ==="
post_json 201 "Create a subject" "{\"name\":\"Chemistry $RUN\",\"description\":\"Organic chemistry\"}" /subjects
SUBJECT_ID="$(new_id)"
post_json 201 "Create a tutor (email is normalised)" "{\"fullName\":\"Demo Tutor\",\"email\":\"Tutor.$RUN@Example.com\",\"hourlyRate\":450.50}" /tutors
TUTOR_ID="$(new_id)"
post_json 201 "Create a student" "{\"fullName\":\"Demo Student\",\"email\":\"student.$RUN@example.com\",\"learningGoal\":\"Pass the exam\"}" /students
STUDENT_ID="$(new_id)"

LESSON="{\"tutorId\":$TUTOR_ID,\"studentId\":$STUDENT_ID,\"subjectId\":$SUBJECT_ID,\"topic\":\"Alkanes\",\"startsAt\":\"2027-03-10T16:00:00+02:00\",\"durationMinutes\":60,\"meetingUrl\":\"https://meet.example.com/demo\"}"
post_json 201 "Create a lesson" "$LESSON" /lessons
LESSON_ID="$(new_id)"

printf 'Lesson notes\n' > "$TMP/notes.txt"
call 201 "Upload a material file (multipart)" -X POST -F 'title=Notes' -F "file=@$TMP/notes.txt" "$BASE/lessons/$LESSON_ID/materials"
MATERIAL_ID="$(new_id)"

echo
echo "=== Read single records ==="
call 200 "Get the lesson with related names" "$BASE/lessons/$LESSON_ID"
call 200 "List materials of the lesson" "$BASE/lessons/$LESSON_ID/materials"
call 200 "Download the material file" "$BASE/materials/$MATERIAL_ID/file"

echo
echo "=== Update ==="
put_json 200 "Update the subject" "{\"name\":\"Chemistry $RUN (advanced)\",\"description\":\"Updated\"}" "/subjects/$SUBJECT_ID"
put_json 200 "Update the tutor" "{\"fullName\":\"Demo Tutor Updated\",\"email\":\"tutor.$RUN@example.com\",\"hourlyRate\":500}" "/tutors/$TUTOR_ID"
put_json 200 "Update the student" "{\"fullName\":\"Demo Student Updated\",\"email\":\"student.$RUN@example.com\"}" "/students/$STUDENT_ID"
put_json 200 "Mark the lesson as completed" "${LESSON%\}},\"status\":\"Completed\",\"notes\":\"Done\"}" "/lessons/$LESSON_ID"
put_json 200 "Rename the material" '{"title":"Notes (final)"}' "/materials/$MATERIAL_ID"
printf 'Second version\n' > "$TMP/notes-v2.txt"
call 200 "Replace the material file" -X PUT -F "file=@$TMP/notes-v2.txt" "$BASE/materials/$MATERIAL_ID/file"

echo
echo "=== Errors: 4xx ==="
call 404 "Unknown record" "$BASE/tutors/99999999"
call 404 "Unknown route" "$BASE/does-not-exist"
post_json 400 "Validation: empty name" '{"name":""}' /subjects
post_json 400 "Validation: bad email and negative rate" '{"fullName":"X","email":"nope","hourlyRate":-1}' /tutors
post_json 400 "Validation: lesson shorter than allowed" "${LESSON/\"durationMinutes\":60/\"durationMinutes\":5}" /lessons
post_json 400 "Validation: unknown status" "${LESSON%\}},\"status\":\"Paused\"}" /lessons
post_json 400 "Malformed JSON" '{"name":' /subjects
post_json 400 "Lesson references a tutor that does not exist" "${LESSON/\"tutorId\":$TUTOR_ID/\"tutorId\":99999999}" /lessons
post_json 409 "Duplicate subject name" "{\"name\":\"Chemistry $RUN (advanced)\"}" /subjects
post_json 409 "Duplicate email differing only by case" "{\"fullName\":\"Copy\",\"email\":\"TUTOR.$RUN@example.com\",\"hourlyRate\":1}" /tutors
call 409 "Delete a subject used by a lesson" -X DELETE "$BASE/subjects/$SUBJECT_ID"
call 409 "Delete a tutor used by a lesson" -X DELETE "$BASE/tutors/$TUTOR_ID"
call 409 "Delete a student used by a lesson" -X DELETE "$BASE/students/$STUDENT_ID"
printf '<script>alert(1)</script>' > "$TMP/evil.html"
call 400 "Upload a disallowed file type" -X POST -F 'title=Evil' -F "file=@$TMP/evil.html" "$BASE/lessons/$LESSON_ID/materials"
call 415 "Wrong content type" -X POST -H 'Content-Type: text/plain' -d 'hello' "$BASE/subjects"

echo
echo "=== Delete ==="
call 204 "Delete the material" -X DELETE "$BASE/materials/$MATERIAL_ID"
call 404 "The deleted material is gone" "$BASE/materials/$MATERIAL_ID"
call 204 "Delete the lesson" -X DELETE "$BASE/lessons/$LESSON_ID"
call 204 "Delete the subject" -X DELETE "$BASE/subjects/$SUBJECT_ID"
call 204 "Delete the tutor" -X DELETE "$BASE/tutors/$TUTOR_ID"
call 204 "Delete the student" -X DELETE "$BASE/students/$STUDENT_ID"
call 404 "The deleted tutor is gone" "$BASE/tutors/$TUTOR_ID"

echo
if [ "$FAILED" -eq 0 ]; then
    echo "All responses matched the expected status codes."
else
    echo "$FAILED response(s) did not match."
    exit 1
fi
