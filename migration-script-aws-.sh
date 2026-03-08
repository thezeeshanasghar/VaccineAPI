#!/usr/bin/env bash
set -euo pipefail

REGION=ap-south-1
SRC=zee
DST=salman
DST_ACCOUNT=253490768942
DST_SUBNET=subnet-0aec555a88abf3d00
DST_KEY=salman-aws-production-backend-frontend-docker

SRC_INSTANCE1=i-08da1e6e28f52a7a8
SRC_INSTANCE2=i-06e326dedf529fff5

# Checklist toggles (set to false to skip a checklist)
RUN_CHECKLIST_1=${RUN_CHECKLIST_1:-true}
RUN_CHECKLIST_2=${RUN_CHECKLIST_2:-true}
RUN_CHECKLIST_3=${RUN_CHECKLIST_3:-true}
RUN_CHECKLIST_4=${RUN_CHECKLIST_4:-true}
RUN_CHECKLIST_5=${RUN_CHECKLIST_5:-true}
RUN_CHECKLIST_6=${RUN_CHECKLIST_6:-true}
RUN_CHECKLIST_7=${RUN_CHECKLIST_7:-true}

# Set to true to allocate and associate new EIPs in destination account.
ASSIGN_EIP=true

# AMI wait tuning for slower snapshot/image creation.
AMI_WAIT_TIMEOUT_MINUTES=${AMI_WAIT_TIMEOUT_MINUTES:-90}
AMI_WAIT_POLL_SECONDS=${AMI_WAIT_POLL_SECONDS:-30}

# These can be pre-exported to resume from a specific checklist.
AMI1=${AMI1:-}
AMI2=${AMI2:-}
NEW_AMI1=${NEW_AMI1:-}
NEW_AMI2=${NEW_AMI2:-}
DST_SG=${DST_SG:-}
NEW_I1=${NEW_I1:-}
NEW_I2=${NEW_I2:-}
EIP_ALLOC1=${EIP_ALLOC1:-}
EIP_ALLOC2=${EIP_ALLOC2:-}

wait_for_images_available() {
  local profile="$1"
  shift
  local image_ids=("$@")
  local elapsed=0
  local timeout_seconds=$((AMI_WAIT_TIMEOUT_MINUTES * 60))

  while true; do
    local state_lines
    state_lines=$(aws ec2 describe-images --profile "$profile" --region "$REGION" --image-ids "${image_ids[@]}" \
      --query 'Images[].{ImageId:ImageId,State:State}' --output text)

    local available_count=0
    while read -r image_id state; do
      [ -z "$image_id" ] && continue
      if [ "$state" = "available" ]; then
        available_count=$((available_count + 1))
      elif [ "$state" = "failed" ]; then
        echo "AMI $image_id entered failed state"
        return 1
      fi
    done <<< "$state_lines"

    if [ "$available_count" -eq "${#image_ids[@]}" ]; then
      echo "All AMIs are available: ${image_ids[*]}"
      return 0
    fi

    if [ "$elapsed" -ge "$timeout_seconds" ]; then
      echo "Timed out waiting for AMIs after ${AMI_WAIT_TIMEOUT_MINUTES} minutes: ${image_ids[*]}"
      echo "Current states:"
      aws ec2 describe-images --profile "$profile" --region "$REGION" --image-ids "${image_ids[@]}" \
        --query 'Images[].{ImageId:ImageId,State:State,StateReason:StateReason.Message}' --output table
      return 1
    fi

    echo "Waiting for AMIs (${profile}): ${image_ids[*]}"
    sleep "$AMI_WAIT_POLL_SECONDS"
    elapsed=$((elapsed + AMI_WAIT_POLL_SECONDS))
  done
}

share_ami_snapshots_with_destination() {
  local source_ami_id="$1"
  local snapshot_ids

  snapshot_ids=$(aws ec2 describe-images --profile "$SRC" --region "$REGION" --image-ids "$source_ami_id" \
    --query 'Images[0].BlockDeviceMappings[].Ebs.SnapshotId' --output text)

  for snapshot_id in $snapshot_ids; do
    [ -z "$snapshot_id" ] && continue
    echo "Sharing snapshot $snapshot_id from $source_ami_id to account $DST_ACCOUNT"
    aws ec2 modify-snapshot-attribute --profile "$SRC" --region "$REGION" \
      --snapshot-id "$snapshot_id" --attribute createVolumePermission \
      --operation-type add --user-ids "$DST_ACCOUNT" >/dev/null
  done
}

checklist_1_prechecks() {
  echo "==== Checklist 1: Prechecks ===="
  aws sts get-caller-identity --profile "$SRC" --region "$REGION" >/dev/null
  aws sts get-caller-identity --profile "$DST" --region "$REGION" >/dev/null
  aws ec2 describe-key-pairs --profile "$DST" --region "$REGION" --key-names "$DST_KEY" >/dev/null

  aws ec2 describe-instances --profile "$SRC" --region "$REGION" --instance-ids "$SRC_INSTANCE1" "$SRC_INSTANCE2" \
    --query 'Reservations[].Instances[].{InstanceId:InstanceId,Name:Tags[?Key==`Name`]|[0].Value,State:State.Name,PrivateIP:PrivateIpAddress,PublicIP:PublicIpAddress,KeyName:KeyName}' \
    --output table

  aws ec2 describe-addresses --profile "$SRC" --region "$REGION" \
    --query 'Addresses[].{PublicIp:PublicIp,AllocationId:AllocationId,InstanceId:InstanceId,AssociationId:AssociationId}' \
    --output table
}

checklist_2_create_source_amis() {
  echo "==== Checklist 2: Create Source AMIs ===="
  if [ -n "$AMI1" ] && [ -n "$AMI2" ]; then
    echo "Using existing source AMIs: $AMI1 $AMI2"
    wait_for_images_available "$SRC" "$AMI1" "$AMI2"
    return
  fi

  AMI1=$(aws ec2 create-image --profile "$SRC" --region "$REGION" \
    --instance-id "$SRC_INSTANCE1" \
    --name "migrate-prod-new-nisar-backend-mysql-$(date +%Y%m%d%H%M%S)" \
    --no-reboot --query 'ImageId' --output text)

  AMI2=$(aws ec2 create-image --profile "$SRC" --region "$REGION" \
    --instance-id "$SRC_INSTANCE2" \
    --name "migrate-run-vaccine-frontends-$(date +%Y%m%d%H%M%S)" \
    --no-reboot --query 'ImageId' --output text)

  echo "Source AMIs: $AMI1 $AMI2"
  wait_for_images_available "$SRC" "$AMI1" "$AMI2"
}

checklist_3_share_and_copy_amis() {
  echo "==== Checklist 3: Share and Copy AMIs ===="
  : "${AMI1:?AMI1 is required. Run checklist 2 or export AMI1=}"
  : "${AMI2:?AMI2 is required. Run checklist 2 or export AMI2=}"

  aws ec2 modify-image-attribute --profile "$SRC" --region "$REGION" \
    --image-id "$AMI1" --launch-permission "Add=[{UserId=$DST_ACCOUNT}]"
  aws ec2 modify-image-attribute --profile "$SRC" --region "$REGION" \
    --image-id "$AMI2" --launch-permission "Add=[{UserId=$DST_ACCOUNT}]"

  # Snapshot permissions are required for cross-account AMI copy.
  share_ami_snapshots_with_destination "$AMI1"
  share_ami_snapshots_with_destination "$AMI2"

  NEW_AMI1=$(aws ec2 copy-image --profile "$DST" --region "$REGION" \
    --source-region "$REGION" --source-image-id "$AMI1" \
    --name "copied-prod-new-nisar-backend-mysql-$(date +%Y%m%d%H%M%S)" \
    --query 'ImageId' --output text)

  NEW_AMI2=$(aws ec2 copy-image --profile "$DST" --region "$REGION" \
    --source-region "$REGION" --source-image-id "$AMI2" \
    --name "copied-run-vaccine-frontends-$(date +%Y%m%d%H%M%S)" \
    --query 'ImageId' --output text)

  echo "Destination AMIs: $NEW_AMI1 $NEW_AMI2"
  wait_for_images_available "$DST" "$NEW_AMI1" "$NEW_AMI2"
}

checklist_4_create_destination_sg() {
  echo "==== Checklist 4: Create Destination SG ===="
  DST_VPC=$(aws ec2 describe-subnets --profile "$DST" --region "$REGION" \
    --subnet-ids "$DST_SUBNET" --query 'Subnets[0].VpcId' --output text)

  DST_SG=$(aws ec2 create-security-group --profile "$DST" --region "$REGION" \
    --group-name "migrated-launch-wizard-4-$(date +%Y%m%d%H%M%S)" \
    --description "Migrated SG from zee launch-wizard-4" \
    --vpc-id "$DST_VPC" --query 'GroupId' --output text)

  aws ec2 authorize-security-group-ingress --profile "$DST" --region "$REGION" \
    --group-id "$DST_SG" --ip-permissions '[{"IpProtocol":"-1","IpRanges":[{"CidrIp":"0.0.0.0/0"}]}]'

  echo "Destination SG: $DST_SG"
}

checklist_5_launch_instances() {
  echo "==== Checklist 5: Launch Destination Instances ===="
  : "${NEW_AMI1:?NEW_AMI1 is required. Run checklist 3 or export NEW_AMI1=}"
  : "${NEW_AMI2:?NEW_AMI2 is required. Run checklist 3 or export NEW_AMI2=}"
  : "${DST_SG:?DST_SG is required. Run checklist 4 or export DST_SG=}"

  NEW_I1=$(aws ec2 run-instances --profile "$DST" --region "$REGION" \
    --image-id "$NEW_AMI1" --instance-type t2.medium \
    --subnet-id "$DST_SUBNET" --security-group-ids "$DST_SG" --key-name "$DST_KEY" \
    --tag-specifications 'ResourceType=instance,Tags=[{Key=Name,Value=prod-new-nisar-backend-mysql}]' \
    --query 'Instances[0].InstanceId' --output text)

  NEW_I2=$(aws ec2 run-instances --profile "$DST" --region "$REGION" \
    --image-id "$NEW_AMI2" --instance-type t2.medium \
    --subnet-id "$DST_SUBNET" --security-group-ids "$DST_SG" --key-name "$DST_KEY" \
    --tag-specifications 'ResourceType=instance,Tags=[{Key=Name,Value=run-vaccine-frontends}]' \
    --query 'Instances[0].InstanceId' --output text)

  echo "New instances in salman: $NEW_I1 $NEW_I2"
  aws ec2 wait instance-running --profile "$DST" --region "$REGION" --instance-ids "$NEW_I1" "$NEW_I2"
}

checklist_6_assign_eips() {
  echo "==== Checklist 6: Allocate and Associate EIPs ===="
  : "${NEW_I1:?NEW_I1 is required. Run checklist 5 or export NEW_I1=}"
  : "${NEW_I2:?NEW_I2 is required. Run checklist 5 or export NEW_I2=}"

  if [ "$ASSIGN_EIP" = true ]; then
    EIP_ALLOC1=$(aws ec2 allocate-address --profile "$DST" --region "$REGION" --domain vpc \
      --query 'AllocationId' --output text)
    EIP_ALLOC2=$(aws ec2 allocate-address --profile "$DST" --region "$REGION" --domain vpc \
      --query 'AllocationId' --output text)

    aws ec2 associate-address --profile "$DST" --region "$REGION" \
      --instance-id "$NEW_I1" --allocation-id "$EIP_ALLOC1" >/dev/null
    aws ec2 associate-address --profile "$DST" --region "$REGION" \
      --instance-id "$NEW_I2" --allocation-id "$EIP_ALLOC2" >/dev/null

    echo "Associated EIP allocations in salman: $EIP_ALLOC1 $EIP_ALLOC2"
  else
    echo "ASSIGN_EIP=false, skipping EIP creation"
  fi
}

checklist_7_summary() {
  echo "==== Checklist 7: Final Summary ===="
  : "${NEW_I1:?NEW_I1 is required. Run checklist 5 or export NEW_I1=}"
  : "${NEW_I2:?NEW_I2 is required. Run checklist 5 or export NEW_I2=}"

  aws ec2 describe-instances --profile "$DST" --region "$REGION" --instance-ids "$NEW_I1" "$NEW_I2" \
    --query 'Reservations[].Instances[].{InstanceId:InstanceId,Name:Tags[?Key==`Name`]|[0].Value,PrivateIP:PrivateIpAddress,PublicIP:PublicIpAddress,State:State.Name,KeyName:KeyName}' \
    --output table

  if [ "$ASSIGN_EIP" = true ] && [ -n "$EIP_ALLOC1" ] && [ -n "$EIP_ALLOC2" ]; then
    aws ec2 describe-addresses --profile "$DST" --region "$REGION" \
      --allocation-ids "$EIP_ALLOC1" "$EIP_ALLOC2" \
      --query 'Addresses[].{AllocationId:AllocationId,PublicIp:PublicIp,InstanceId:InstanceId,AssociationId:AssociationId}' \
      --output table
  fi
}

run_checklist() {
  [ "$RUN_CHECKLIST_1" = true ] && checklist_1_prechecks
  [ "$RUN_CHECKLIST_2" = true ] && checklist_2_create_source_amis
  [ "$RUN_CHECKLIST_3" = true ] && checklist_3_share_and_copy_amis
  [ "$RUN_CHECKLIST_4" = true ] && checklist_4_create_destination_sg
  [ "$RUN_CHECKLIST_5" = true ] && checklist_5_launch_instances
  [ "$RUN_CHECKLIST_6" = true ] && checklist_6_assign_eips
  [ "$RUN_CHECKLIST_7" = true ] && checklist_7_summary
}

run_checklist