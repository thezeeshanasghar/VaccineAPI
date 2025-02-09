#!/bin/bash

# Define variables
CONTAINER_NAME=vaccineapi-db-1
BACKUP_PATH=/home/ec2-user
BACKUP_FILE=vaccineapi_backup_$(date +%F).sql.gz
S3_BUCKET_NAME=vaccine-api-daily-backup

# Run mysqldump inside the container and compress the output
docker exec $CONTAINER_NAME sh -c "mysqldump -u root -ptest vaccineapi | gzip > /tmp/$BACKUP_FILE"

# Copy the compressed backup to the home directory of ec2-user
docker cp $CONTAINER_NAME:/tmp/$BACKUP_FILE $BACKUP_PATH/$BACKUP_FILE

# Upload the backup to S3
aws s3 cp $BACKUP_PATH/$BACKUP_FILE s3://$S3_BUCKET_NAME/$BACKUP_FILE