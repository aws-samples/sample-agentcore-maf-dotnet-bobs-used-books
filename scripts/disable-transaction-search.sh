#!/usr/bin/env bash
# Reverts scripts/enable-transaction-search.sh. It acts only when that script enabled
# Transaction Search (its Logs resource policy exists); otherwise the account-wide setting
# belongs to someone else and is left unchanged.
set -euo pipefail

: "${AWS_REGION:?Set AWS_REGION before disabling Transaction Search.}"
POLICY_NAME="BobsBooksTransactionSearch"
SAMPLE_TAG_KEY="sample"
SAMPLE_TAG_VALUE="bobs-books-agentcore"

if [ -z "$(aws logs describe-resource-policies --region "$AWS_REGION" \
    --query "resourcePolicies[?policyName=='$POLICY_NAME'].policyName" --output text)" ]; then
  echo "Transaction Search was not enabled by this sample in $AWS_REGION; leaving it unchanged."
  exit 0
fi

identity_arn="$(aws sts get-caller-identity --query Arn --output text)"
partition="$(cut -d: -f2 <<<"$identity_arn")"
account="$(cut -d: -f5 <<<"$identity_arn")"
log_group_arn() { printf 'arn:%s:logs:%s:%s:log-group:%s' "$partition" "$AWS_REGION" "$account" "$1"; }

echo "Disabling CloudWatch Transaction Search in $AWS_REGION (enabled by this sample)."
for _ in $(seq 1 180); do
  [ "$(aws xray get-trace-segment-destination --region "$AWS_REGION" --query Status --output text)" != "PENDING" ] && break
  echo "Waiting for a pending X-Ray trace destination change to finish..."
  sleep 10
done
if [ "$(aws xray get-trace-segment-destination --region "$AWS_REGION" --query Destination --output text)" = "CloudWatchLogs" ]; then
  aws xray update-trace-segment-destination --region "$AWS_REGION" --destination XRay >/dev/null
fi
aws logs delete-resource-policy --region "$AWS_REGION" --policy-name "$POLICY_NAME"
for log_group in aws/spans /aws/application-signals/data; do
  tag="$(aws logs list-tags-for-resource --region "$AWS_REGION" \
    --resource-arn "$(log_group_arn "$log_group")" \
    --query "tags.$SAMPLE_TAG_KEY" --output text 2>/dev/null || true)"
  if [ "$tag" = "$SAMPLE_TAG_VALUE" ]; then
    aws logs delete-log-group --region "$AWS_REGION" --log-group-name "$log_group"
    echo "Deleted log group $log_group"
  fi
done
echo "The X-Ray trace destination change back to X-Ray can take several minutes to finish."
