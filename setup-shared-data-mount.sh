#!/usr/bin/env bash
set -euo pipefail

UUID="01DBCCD51BB99890"
MOUNT_POINT="/mnt/data"
FSTAB_LINE="UUID=${UUID} ${MOUNT_POINT} ntfs3 uid=1000,gid=1000,umask=022,windows_names,nofail 0 0"
FSTAB_COMMENT="# Shared NTFS data partition for Ubuntu/Windows"

echo "Creating mount point: ${MOUNT_POINT}"
mkdir -p "${MOUNT_POINT}"

if ! grep -q "${UUID}" /etc/fstab; then
  backup="/etc/fstab.codex.bak.$(date +%Y%m%d-%H%M%S)"
  echo "Backing up /etc/fstab to ${backup}"
  cp /etc/fstab "${backup}"

  printf '\n%s\n%s\n' "${FSTAB_COMMENT}" "${FSTAB_LINE}" >> /etc/fstab
  echo "Added mount entry to /etc/fstab"
else
  echo "An entry for UUID ${UUID} already exists in /etc/fstab"
fi

echo "Mounting all filesystems from /etc/fstab"
mount -a

echo "Mounted filesystem:"
findmnt "${MOUNT_POINT}"

echo "Top-level contents of ${MOUNT_POINT}:"
ls -la "${MOUNT_POINT}" | sed -n '1,20p'
