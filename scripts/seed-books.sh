#!/usr/bin/env bash
set -euo pipefail

: "${AWS_REGION:?Set AWS_REGION before seeding.}"
TABLE_NAME="${TABLE_NAME:-BookInventory${STACK_POSTFIX:-}}"
GSI_MONTH="$(date -u +%Y%m)"
NOW="$(date -u +%Y-%m-%dT%H:%M:%SZ)"

put_book() {
  local id="$1" name="$2" author="$3" isbn="$4" publisher="$5"
  local book_type="$6" genre="$7" condition="$8" price="$9"
  local quantity="${10}" summary="${11}" year="${12}"

  aws dynamodb put-item --region "$AWS_REGION" --table-name "$TABLE_NAME" --item "$(cat <<JSON
{
  "BookId":{"S":"$id"},"Name":{"S":"$name"},"Author":{"S":"$author"},
  "ISBN":{"S":"$isbn"},"Publisher":{"S":"$publisher"},"BookType":{"S":"$book_type"},
  "Genre":{"S":"$genre"},"Condition":{"S":"$condition"},"Price":{"N":"$price"},
  "Quantity":{"N":"$quantity"},"Summary":{"S":"$summary"},"Year":{"N":"$year"},
  "CreatedBy":{"S":"sample-seed"},"CreatedOn":{"S":"$NOW"},"LastUpdated":{"S":"$NOW"},
  "GSI1PK":{"S":"$GSI_MONTH"},"GSI1SK":{"S":"$id"}
}
JSON
)" >/dev/null
  printf 'Seeded %s\n' "$name"
}

put_book "34b1d665-3ef0-4921-ada0-94954b2e2b2f" "The Locked Room Ledger" "Mara Ellison" "9781736204101" "Northbridge Press" "Hardcover" "Mystery" "Like New" "7.49" "4" "A forensic accountant follows clues hidden in an old bookseller's ledger." "2022"
put_book "abc30e07-4db3-4efd-bb40-7eaf8fb333c9" "Midnight at Ashcroft" "Daniel Rook" "9781736204102" "Hawthorn House" "Hardcover" "Mystery" "Good" "9.25" "7" "A village archivist investigates a disappearance during a winter blackout." "2020"
put_book "681909c1-ab92-442d-9517-b7727bfbd6b2" "Orbit of Glass" "Priya Sen" "9781736204103" "Zenith Books" "Hardcover" "Science Fiction" "Very Good" "14.99" "3" "Engineers aboard an orbital habitat race to repair a failing shield." "2023"
put_book "09ef49f5-5d75-46be-8eaf-e80bc175f5e3" "The Last Harbor Signal" "Evan Cole" "9781736204104" "Blue Quay Publishing" "Paperback" "Thriller" "Acceptable" "6.95" "8" "A radio operator receives a distress call from an abandoned lighthouse." "2019"
put_book "df8f3244-5850-4fae-ab0b-b643a7f2bff0" "Practical Serverless .NET" "Nina Kapoor" "9781736204105" "Builder Press" "Hardcover" "Technology" "Like New" "18.50" "5" "Patterns for building event-driven .NET applications on AWS." "2025"
