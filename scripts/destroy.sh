#!/usr/bin/env bash
set -euo pipefail

export PATH="$HOME/.dotnet:$PATH"
export DOTNET_ROLL_FORWARD=Major

cat <<'NOTICE'
Destroying all sample stacks. The default environment retains the DynamoDB table,
cover-page bucket, and Runtime log group. Review and remove retained data separately
only when it is no longer needed. The shared CDK bootstrap stack is not destroyed.
NOTICE

npx -y aws-cdk@2 destroy --all --force
