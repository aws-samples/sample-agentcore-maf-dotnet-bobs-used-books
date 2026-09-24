#!/usr/bin/env bash
# One-time, account-wide setup for this Region: enable CloudWatch Transaction Search so
# X-Ray accepts the Runtime's OpenTelemetry (OTLP) spans and stores them in aws/spans.
# Idempotent: does nothing if Transaction Search is already enabled.
set -euo pipefail

: "${AWS_REGION:?Set AWS_REGION before enabling Transaction Search.}"
POLICY_NAME="BobsBooksTransactionSearch"
SAMPLE_TAG_KEY="sample"
SAMPLE_TAG_VALUE="bobs-books-agentcore"

destination_field() {
  aws xray get-trace-segment-destination --region "$AWS_REGION" --query "$1" --output text
}

# X-Ray rejects updates while a previous change is PENDING (for example, right after
# scripts/destroy.sh disabled Transaction Search). Wait up to 30 minutes for it to settle.
wait_while_pending() {
  for _ in $(seq 1 180); do
    if [ "$(destination_field Status)" != "PENDING" ]; then
      return 0
    fi
    echo "Waiting for a pending X-Ray trace destination change to finish..."
    sleep 10
  done
  echo "The X-Ray trace destination is still PENDING after 30 minutes; try again later." >&2
  exit 1
}

wait_while_pending
if [ "$(destination_field Destination)" = "CloudWatchLogs" ]; then
  echo "CloudWatch Transaction Search is already enabled in $AWS_REGION; leaving it unchanged."
  exit 0
fi

identity_arn="$(aws sts get-caller-identity --query Arn --output text)"
partition="$(cut -d: -f2 <<<"$identity_arn")"
account="$(cut -d: -f5 <<<"$identity_arn")"
log_group_exists() {
  [ -n "$(aws logs describe-log-groups --region "$AWS_REGION" --log-group-name-prefix "$1" \
    --query "logGroups[?logGroupName=='$1'].logGroupName" --output text)" ]
}

# Remember which span log groups already exist, so only the ones X-Ray creates now are
# tagged for removal by scripts/disable-transaction-search.sh.
new_log_groups=()
for log_group in aws/spans /aws/application-signals/data; do
  log_group_exists "$log_group" || new_log_groups+=("$log_group")
done

aws logs put-resource-policy --region "$AWS_REGION" --policy-name "$POLICY_NAME" \
  --policy-document "$(cat <<JSON
{
  "Version": "2012-10-17",
  "Statement": [{
    "Sid": "TransactionSearchXRayAccess",
    "Effect": "Allow",
    "Principal": {"Service": "xray.amazonaws.com"},
    "Action": "logs:PutLogEvents",
    "Resource": [
      "arn:$partition:logs:$AWS_REGION:$account:log-group:aws/spans:*",
      "arn:$partition:logs:$AWS_REGION:$account:log-group:/aws/application-signals/data:*"
    ],
    "Condition": {
      "ArnLike": {"aws:SourceArn": "arn:$partition:xray:$AWS_REGION:$account:*"},
      "StringEquals": {"aws:SourceAccount": "$account"}
    }
  }]
}
JSON
)" >/dev/null

aws xray update-trace-segment-destination --region "$AWS_REGION" \
  --destination CloudWatchLogs >/dev/null

wait_while_pending
status="$(destination_field Status)"
if [ "$status" != "ACTIVE" ]; then
  echo "Transaction Search status is $status; check the CloudWatch console." >&2
  exit 1
fi

# X-Ray creates these AWS-reserved log groups. Tag the new ones so
# scripts/disable-transaction-search.sh removes only what this script caused to exist.
for log_group in ${new_log_groups[@]+"${new_log_groups[@]}"}; do
  for _ in $(seq 1 12); do
    log_group_exists "$log_group" && break
    sleep 5
  done
  aws logs tag-resource --region "$AWS_REGION" \
    --resource-arn "arn:$partition:logs:$AWS_REGION:$account:log-group:$log_group" \
    --tags "$SAMPLE_TAG_KEY=$SAMPLE_TAG_VALUE" 2>/dev/null ||
    echo "Could not tag $log_group; scripts/destroy.sh will leave it in place." >&2
done
echo "Enabled CloudWatch Transaction Search in $AWS_REGION. New spans can take up to 10 minutes to appear."
