# Local packaging and verification

## Acceptance checklist

Use [Build-SamplePackages.ps1](../samples/Build-SamplePackages.ps1) for the standard EXE, offline EXE, and checksums. Use a fresh install parent for these checks.

Set `GAMEINSTALLER_SAMPLE_ROOT` to an empty local folder for an isolated run. This puts demo fixtures and product state under `Fixtures` and `Products`. The sample simulates firewall changes. For a real current-user Desktop, Start menu, and Installed Apps check, use a packaged sample and set `GAMEINSTALLER_SAMPLE_WINDOWS=1`. Use the wizard to uninstall the scratch product at the end.

1. Run the standalone sample, install, and play. Choose **Write sample save**, close the game, remove the install, and confirm its `Data\sample-save.txt` remains.
2. Run the NeonLoop-style sample. Install and play the base with the optional scene unavailable. Note the path under **Source**, then verify that installer payloads and prepared files appear under `Installs`, while the mock stock folder still contains only its fixture files.
3. Enable **Prepare optional scene**. Pick a wrong file, then the matching file. Set `GAMEINSTALLER_SAMPLE_SLOW_WORK=1` to pause long enough to stop between the two checkpoints. Retry, confirm the first checkpoint was reused, and finish the second. If offered, choose limited play to open the prepared base.
4. After an install, use the log and product folder to locate the active generation. Add an untracked file and change `mod-settings.txt`. Remove the product and confirm the user files and setup logs remain. The original mock game remains in the fixture folder.
5. For update recovery, use the same sample product ID with a higher version and a deliberately missing required payload. Confirm the failed update leaves the previous version active and launchable. For remote acquisition, configure a small HTTPS asset you control and check a failed transfer, a fresh approval, and verified cache reuse.
6. Compare Easy and Custom reviews. Confirm the bonus pack ZIP is verified automatically, and skip the manually supplied scene. The bonus pack should install and the result should list the skipped scene. Enable local-only mode and confirm the download prompt offers local files without a download action.
7. In the standard sample, select the prepared scene and opt into the mock compatibility check. Set `GAMEINSTALLER_SAMPLE_FAIL_PREFERRED=1` to exercise CPU fallback. Try the separate approval and decline paths. In the offline sample, confirm the selected packs and standard worker run on embedded files.
8. Confirm **Install** starts disabled on the review page. Leave the terms unchecked, go back, and confirm setup created no product files. Accept on a new review and check for a receipt under `Agreements`.
9. Compare the four stages with the reference at the default size and larger sizes. Check keyboard navigation, the in-window path picker, and the expandable bottom console. Set `GAMEINSTALLER_SAMPLE_MANUAL_STEAM=1` to trigger the mock Steam picker.
10. Inspect Custom’s two outbound block targets in the preview. Close the preview to leave policy alone. Confirm Easy produces no `firewall-preview.log`. Cancel on each of the three uninstall pages and verify the files stay until the final confirmation.

## Future GitHub snapshot

- When publishing is authorized, stage the solution, project and C# files, `.gitignore`, public documentation, `LICENSE`, `HOBBY-TERMS.txt`, and intentional sample fixtures in a fresh directory. Include `LICENSE` and `HOBBY-TERMS.txt` in the binary ZIP, plus any applicable third-party notices. Offer the matching source snapshot with each versioned release.
- Review the staged source and versioned publish ZIP for credentials, developer-local paths, private payloads, copied games, and logs. The release ZIP should contain only the runnable installer, cleared payloads, and the instructions needed to use it. Only cleared optional packs belong in separate ZIPs.
- Follow the workspace publishing workflow. Use the signed-in Browser as `goodtripgoodtip`, start with a private repository, and use a project-specific release tag. Keep repository and release links in the staged README. Confirm the owner, visibility, and exact release ZIP after publishing. Keep the source workspace independent of the publishing repository.
