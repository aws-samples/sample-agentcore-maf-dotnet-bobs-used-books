#!/usr/bin/env bash
# One-time, account-wide setup for this Region: enable CloudWatch Transaction Search so
# X-Ray accepts the Runtime's OpenTelemetry (OTLP) spans and stores them in aws/spans.
# Idempotent: does nothing if Transaction Search is already enabled.
set -euo pipefail

: "${AWS_REGION:?Set AWS_REGION before enabling Transaction Search.}"
POLICY_NAME="BobsBooksTransactionSearch"
SAMPLE_TAG_KEY="sample"
SAMPLE_TAG_VALUE="bobs-books-agentcore"

destination="$(aws xray get-trace-segment-destination --region "$AWS_REGION" \
  --query Destination --output text)"
if [ "$destination" = "CloudWatchLogs" ]; then
  echo "CloudWatch Transaction Search is already enabled in $AWS_REGION; leaving it unchanged."
  exit 0
fi

identity_arn="$(aws sts get-caller-identity --query Arn --output text)"
partition="$(cut -d: -f2 <<<"$identity_arn")"
account="$(cut -d: -f5 <<<"$identity_arn")"

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

for _ in $(seq 1 60); do
  status="$(aws xray get-trace-segment-destination --region "$AWS_REGION" \
    --query Status --output text)"
  if [ "$status" = "ACTIVE" ]; then
    # X-Ray creates these AWS-reserved log groups. Tag them so
    # scripts/disable-transaction-search.sh removes only what this script enabled.
    for log_group in aws/spans /aws/application-signals/data; do
      aws logs tag-resource --region "$AWS_REGION" \
        --resource-arn "arn:$partition:logs:$AWS_REGION:$account:log-group:$log_group" \
        --tags "$SAMPLE_TAG_KEY=$SAMPLE_TAG_VALUE" 2>/dev/null || true
    done
    echo "Enabled CloudWatch Transaction Search in $AWS_REGION. New spans can take up to 10 minutes to appear."
    exit 0
  fi
  sleep 10
done

echo "Transaction Search is still $status after 10 minutes; check the CloudWatch console." >&2
exit 1
