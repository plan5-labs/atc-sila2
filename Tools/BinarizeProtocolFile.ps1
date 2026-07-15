function GetProtocolFile() {
    # Load assembly
    Add-Type -AssemblyName System.Windows.Forms

    $FileDialog = New-Object System.Windows.Forms.OpenFileDialog
    $FileDialog.Filter = "XML Files|*.xml|All Files|*.*"
    $FileDialog.InitialDirectory = Get-Location
    $FileDialog.Title = "Select protocol file."

    if ($FileDialog.ShowDialog() -ne [System.Windows.Forms.DialogResult]::OK) {
        # Canceled
        $FileDialog.Dispose()
        exit
    }

    $TargetFileFullPath = $FileDialog.FileName
    $FileDialog.Dispose()
    return $TargetFileFullPath
}

# Select protocol file.
$ProtocolFile = GetProtocolFile

# Binarize the protocol.
$BinarizedProtocol = [Convert]::ToBase64String([IO.File]::ReadAllBytes($ProtocolFile))
Set-Clipboard $BinarizedProtocol
