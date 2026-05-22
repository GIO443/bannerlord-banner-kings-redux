#!/usr/bin/env pwsh
<#
    BK Gauntlet prefab linter.

    Scans BannerKings/_Module/GUI/Prefabs for two grounded problem patterns
    that have actually bitten this mod:

      1. Malformed XML — reported as an error. A prefab that will not parse
         will not load in-game.

      2. HeightSizePolicy="StretchToParent" + StackLayout.LayoutMethod=
         "VerticalBottomToTop" on a panel with MORE THAN ONE child — the
         "bottom-pile" smell. A stretch-height panel laying children
         bottom-to-top piles its content against the bottom edge, leaving
         the top empty and overlapping anything else anchored there. This
         broke the campaign-start option cards and seven other prefabs.
         Reported as a WARNING, never a build failure: the combo is
         legitimate for a genuinely bottom-anchored list, so a human still
         has to confirm. It just makes the panel impossible to miss.

    Render-blind safety net: it cannot tell you a screen looks ugly, but it
    catches the structural class of bug without anyone launching the game.

    Run locally:  pwsh tools/lint-prefabs.ps1
#>
param(
    [string]$PrefabRoot = 'BannerKings/_Module/GUI/Prefabs'
)

$errors = 0
$warnings = 0

if (-not (Test-Path $PrefabRoot)) {
    Write-Host "lint-prefabs: prefab root not found: $PrefabRoot"
    exit 1
}

$files = Get-ChildItem -Path $PrefabRoot -Recurse -Filter *.xml | Sort-Object FullName
$root  = (Get-Location).Path

foreach ($file in $files) {
    $rel = [System.IO.Path]::GetRelativePath($root, $file.FullName)

    # --- Check 1: XML well-formedness -------------------------------------
    $doc = [System.Xml.XmlDocument]::new()
    try {
        $doc.Load($file.FullName)
    }
    catch {
        Write-Host "ERROR  $rel"
        Write-Host "       malformed XML: $($_.Exception.Message)"
        $errors++
        continue
    }

    # --- Check 2: StretchToParent + VerticalBottomToTop bottom-pile -------
    foreach ($node in $doc.SelectNodes('//*')) {
        if ($node.GetAttribute('HeightSizePolicy') -ne 'StretchToParent') { continue }
        if ($node.GetAttribute('StackLayout.LayoutMethod') -ne 'VerticalBottomToTop') { continue }

        # Count element children under the node's <Children> wrapper.
        $childrenEl = $node.ChildNodes |
            Where-Object { $_.NodeType -eq 'Element' -and $_.Name -eq 'Children' } |
            Select-Object -First 1
        $childCount = 0
        if ($null -ne $childrenEl) {
            $childCount = @($childrenEl.ChildNodes | Where-Object { $_.NodeType -eq 'Element' }).Count
        }

        if ($childCount -gt 1) {
            $id = $node.GetAttribute('Id')
            $label = if ($id) { "<$($node.Name) Id=`"$id`">" } else { "<$($node.Name)>" }
            Write-Host "WARN   $rel"
            Write-Host "       $label has HeightSizePolicy=StretchToParent + VerticalBottomToTop"
            Write-Host "       with $childCount children — bottom-pile risk. If the content is meant"
            Write-Host "       to read top-down, switch to VerticalTopToBottom."
            $warnings++
        }
    }
}

Write-Host ''
Write-Host "lint-prefabs: $($files.Count) prefab(s) scanned, $errors error(s), $warnings warning(s)."

# Advisory linter: always exits 0 so it can never unexpectedly redden CI.
# The findings are surfaced in the run log for a human to act on. To turn
# malformed XML into a hard gate later, swap this for:
#     exit ([int]($errors -gt 0))
exit 0
