# PC-to-PC inbox qualification

This workflow needs an existing approved Windows share on the **new PC**, reachable from both PCs using the current Windows identity. Ethernet/RJ45 must already provide working addressing/name resolution. RoboTransfer does not create a share, open ports or change protection settings.

## New PC

1. Open RoboTransfer and choose **This is my new PC**.
2. Analyze the PC. Choose the receiving employee and a reachable approved share hosted by that computer (computer-name/FQDN UNC).
3. Choose **Prepare my receiving folder**.
4. Keep the generated inbox path and eight-character identifier visible. The identifier is for comparison, not authentication.

## Old PC

1. Choose **This is my old PC**, analyze, and select the source employee.
2. Expand **Send to a prepared new PC (RJ45 / network)**.
3. Paste the complete inbox path from the new PC and check the receiving PC.
4. Compare its employee/PC name and identifier against the new PC's screen, enter the identifier and confirm the recipient.
5. Choose **Use this receiving PC**. Select folders, scan and review the plan, then confirm and start the transfer.
6. Run file verification after copying. Save the final report after reviewing the result.

Both endpoints' local policy must authorize the base share. The new PC shows sender-reported counts and status, automatically refreshed every two seconds. A stalled or unavailable update is not proof of completion. After verification/report generation, read `report.json`/`report.html` inside the inbox. Incoming employee files remain under `Files/<KnownFolder>`; this workflow does not restore Windows settings or merge into redirected profile folders.

## Qualification cases

- Correct recipient and two separate employee sessions; wrong recipient/code and changed descriptor.
- Insufficient/unknown free space, denied access, missing/revoked share policy.
- Real small files, 1 GB/10 GB/100+ GB datasets, high file counts, duplicates and preservation.
- Cable disconnect/reconnect, sender cancellation/restart, existing session revalidation and resume.
- OneDrive placeholders and locked PST/OST handling.
- Correct, warning, failed/cancelled verification and report outcomes on both screens.
- Fresh install/upgrade/repair/uninstall and endpoint trust/EDR behavior.

Close an unused inbox to reject new senders. Once claimed, leave it available for completion/recovery; **Monitor another migration** only leaves the monitor and preserves evidence. After an application restart, analyze the receiver and reopen its existing inbox path. On the source, use the existing unfinished-session recovery controls with the same policy, profile and receiver.

If no approved hosted share is reachable, the receiver cannot be prepared. A cable alone does not remove SMB restrictions. Record the failure and have the route owner provide an approved mechanism before repeating physical qualification.
