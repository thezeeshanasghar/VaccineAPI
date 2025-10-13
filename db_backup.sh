#!/bin/bash

# Add these lines at the top if you have a .env file
set -a
source /home/ec2-user/VaccineAPI/.env
set +a

# Or set them directly here if you know the values
MYSQL_USER=${DB_USER:-root}
MYSQL_PASSWORD=${DB_PASSWORD:-test}
MYSQL_DATABASE=${DB_NAME:-vaccineapi}

# Define variables
CONTAINER_NAME=vaccineapi-db-1
BACKUP_PATH=/home/ec2-user
BACKUP_FILE=vaccineapi_backup_$(date +%F).sql.gz
S3_BUCKET_NAME=vaccine-api-daily-backup

# Run mysqldump inside the container and compress the output
docker exec $CONTAINER_NAME sh -c "mysqldump -u $MYSQL_USER -p$MYSQL_PASSWORD $MYSQL_DATABASE | gzip > /tmp/$BACKUP_FILE"

# Copy the compressed backup to the home directory of ec2-user
docker cp $CONTAINER_NAME:/tmp/$BACKUP_FILE $BACKUP_PATH/$BACKUP_FILE

# Upload the backup to S3
aws s3 cp $BACKUP_PATH/$BACKUP_FILE s3://$S3_BUCKET_NAME/$BACKUP_FILE