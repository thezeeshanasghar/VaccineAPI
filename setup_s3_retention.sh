#!/bin/bash

# Define variables
S3_BUCKET_NAME=vaccine-api-daily-backup

# Create a JSON file for the lifecycle configuration
cat > lifecycle.json << 'EOL'
{
  "Rules": [
    {
      "ID": "Delete-after-14-days",
      "Status": "Enabled",
      "Prefix": "",
      "Expiration": {
        "Days": 14
      }
    }
  ]
}
EOL

# Apply the lifecycle configuration to the S3 bucket
aws s3api put-bucket-lifecycle-configuration --bucket $S3_BUCKET_NAME --lifecycle-configuration file://lifecycle.json

# Verify the configuration
aws s3api get-bucket-lifecycle-configuration --bucket $S3_BUCKET_NAME

echo "S3 bucket retention policy set to 14 days (2 weeks)"

# Clean up
rm lifecycle.json
