#!/usr/bin/env bash
# Destroys every sample stack and removes what the stacks leave behind:
#   - the retained DynamoDB table and versioned cover-page bucket (keep them with --keep-data),
#   - log groups and account settings that AWS services create on the sample's behalf,
#   - CloudWatch Transaction Search, if scripts/enable-transaction-search.sh enabled it.
# The shared CDK bootstrap stack (CDKToolkit) is never touched.
set -euo pipefail

export PATH="$HOME/.dotnet:$PATH"
export DOTNET_ROLL_FORWARD=Major
: "${AWS_REGION:?Set AWS_REGION before destroying the sample.}"
export CDK_DEFAULT_REGION="${CDK_DEFAULT_REGION:-$AWS_REGION}"
export CDK_DEFAULT_ACCOUNT="${CDK_DEFAULT_ACCOUNT:-$(aws sts get-caller-identity --query Account --output text)}"

keep_data=false
[ "${1:-}" = "--keep-data" ] && keep_data=true

postfix="${STACK_POSTFIX:-}"
api_stack="BobsBooksApi${postfix}"
table="BookInventory${postfix}"
bucket="bookinventoryservice-coverpage-images-${CDK_DEFAULT_ACCOUNT}-${AWS_REGION}${postfix}"

# Read what the API stack created before it is deleted.
rest_api_id="$(aws cloudformation describe-stacks --region "$AWS_REGION" --stack-name "$api_stack" \
  --query "Stacks[0].Outputs[?OutputKey=='BookInventoryServiceRestApiIdOutput${postfix}'].OutputValue | [0]" \
  --output text 2>/dev/null || true)"
api_stack_created="$(aws cloudformation describe-stacks --region "$AWS_REGION" --stack-name "$api_stack" \
  --query 'Stacks[0].CreationTime' --output text 2>/dev/null || true)"

echo "Destroying stacks BobsBooksAuth${postfix}, ${api_stack}, AgentCoreGateway${postfix}, AgentRuntime${postfix}."
if [ "$keep_data" = false ]; then
  echo "Then permanently deleting DynamoDB table ${table} and S3 bucket ${bucket} (every object version)."
fi
echo "The shared CDK bootstrap stack (CDKToolkit) is not destroyed."

npx -y aws-cdk@2 destroy --all --force

if [ "$keep_data" = false ]; then
  if aws dynamodb describe-table --region "$AWS_REGION" --table-name "$table" >/dev/null 2>&1; then
    aws dynamodb delete-table --region "$AWS_REGION" --table-name "$table" >/dev/null
    aws dynamodb wait table-not-exists --region "$AWS_REGION" --table-name "$table"
    echo "Deleted DynamoDB table ${table}"
  fi

  if aws s3api head-bucket --bucket "$bucket" >/dev/null 2>&1; then
    versions_query='[Versions[].{Key:Key,VersionId:VersionId}, DeleteMarkers[].{Key:Key,VersionId:VersionId}][]'
    while [ "$(aws s3api list-object-versions --bucket "$bucket" --max-items 1000 \
        --query "length($versions_query)" --output text)" != "0" ]; do
      aws s3api delete-objects --bucket "$bucket" --delete "$(aws s3api list-object-versions \
        --bucket "$bucket" --max-items 1000 \
        --query "{Objects: $versions_query, Quiet: \`true\`}" --output json)" >/dev/null
    done
    aws s3api delete-bucket --bucket "$bucket"
    echo "Deleted S3 bucket ${bucket}"
  fi
fi

delete_log_group() {
  if [ -n "$(aws logs describe-log-groups --region "$AWS_REGION" --log-group-name-prefix "$1" \
      --query "logGroups[?logGroupName=='$1'].logGroupName" --output text)" ]; then
    aws logs delete-log-group --region "$AWS_REGION" --log-group-name "$1"
    echo "Deleted log group $1"
  fi
}

# API Gateway creates the execution log group itself, so the stack cannot delete it.
if [ -n "$rest_api_id" ] && [ "$rest_api_id" != "None" ]; then
  delete_log_group "API-Gateway-Execution-Logs_${rest_api_id}/prod"
fi

# The API stack set the account-wide API Gateway CloudWatch role to its own role, which the
# stack has now deleted. Clear the setting only if it still points at this sample's role.
cloudwatch_role="$(aws apigateway get-account --region "$AWS_REGION" --query cloudwatchRoleArn --output text)"
case "$cloudwatch_role" in
  *":role/${api_stack}-BookInventoryApiCloudWatchRole"*)
    aws apigateway update-account --region "$AWS_REGION" \
      --patch-operations op=replace,path=/cloudwatchRoleArn,value= >/dev/null
    echo "Cleared the API Gateway account CloudWatch role setting"
    # API Gateway creates this log group when the role is first set.
    welcome_created="$(aws logs describe-log-groups --region "$AWS_REGION" \
      --log-group-name-prefix /aws/apigateway/welcome \
      --query "logGroups[?logGroupName=='/aws/apigateway/welcome'].creationTime | [0]" --output text)"
    if [ "$welcome_created" != "None" ] && [ -n "$api_stack_created" ] && \
       [ "$welcome_created" -ge "$(node -e 'process.stdout.write(String(Date.parse(process.argv[1])))' "$api_stack_created")" ]; then
      delete_log_group /aws/apigateway/welcome
    fi
    ;;
esac

"$(dirname "$0")/disable-transaction-search.sh"
