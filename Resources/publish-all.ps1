# PowerShell script to run all publishing profiles for a Visual Studio .NET project. Also automatically renames resulting binary file to include version number.
# Author: ThioJoe (https://github.com/ThioJoe)

# VERSION: 1.2 (Updated 4/4/25)

# --------------------------------------------------------------

# Tip: Make this script easier to run by adding it as an "External Tool" so it will show up in the "Tools" dropdown menu.
#
# You can do this by:
#      1. Going to Tools > External Tools > Add
#      2. For 'Command' put 'powershell.exe'
#      3. For 'Arguments' use the -File parameter with the relative path (relative to project directory) and filename you have this script, for example:
#			-File "Resources\publish-all.ps1"
#      4. Be sure to set the "Initial Directory" box to:
#            $(ProjectDir)
#
# --------------------------------------------------------------

# Helper function to get a property value from the first .csproj file found
function Get-CsProjProperty {
    param (
        [string]$PropertyName
    )

    # Find the first .csproj file in the current directory
    $csprojFile = Get-ChildItem -Filter "*.csproj" -File | Select-Object -First 1
    if (-not $csprojFile) {
        Write-Warning "No .csproj file found in the current directory."
        return $null
    }

    try {
        [xml]$csprojContent = Get-Content $csprojFile.FullName -Raw

        # --- Handle Potential XML Namespaces (Common in SDK-style projects) ---
        $namespaceURI = $csprojContent.DocumentElement.NamespaceURI
        $xpath = ""
        $node = $null

        if (-not [string]::IsNullOrWhiteSpace($namespaceURI)) {
            # If a default namespace exists, we MUST use a namespace manager
            $nsMgr = New-Object System.Xml.XmlNamespaceManager($csprojContent.NameTable)
            # Use a placeholder prefix (like 'msb') for the default namespace in XPath queries
            $nsMgr.AddNamespace("msb", $namespaceURI)
            # Construct XPath to find the property within any PropertyGroup, using the namespace prefix
            $xpath = "//msb:PropertyGroup/msb:$PropertyName"
            Write-Verbose "Using XML namespace '$namespaceURI' with prefix 'msb'. XPath: $xpath"
            # Select the first node matching the XPath query using the namespace manager
            $node = $csprojContent.SelectSingleNode($xpath, $nsMgr)
        } else {
            # If no namespace is defined (less common for modern projects)
            $xpath = "//PropertyGroup/$PropertyName"
             Write-Verbose "No XML namespace found. Using XPath: $xpath"
            # Select the first node matching the XPath query directly
            $node = $csprojContent.SelectSingleNode($xpath)
        }
        # --- End Namespace Handling ---

        # Check if a node was found
        if ($node) {
             $propertyValue = $node.'#text'.Trim() # Get the inner text and trim whitespace
             Write-Verbose "Found node for '$PropertyName' in $($csprojFile.Name). Value: '$propertyValue'"
             return $propertyValue
        } else {
            # Property wasn't found with the primary XPath query
            Write-Verbose "Property '$PropertyName' not found using XPath '$xpath' in $($csprojFile.Name)."
            # Optional: Could add fallback logic here if needed (e.g., check different paths), but often the above is sufficient if the property exists.
            return $null
        }
    } catch {
        Write-Warning "Error reading or parsing $($csprojFile.Name): $($_.Exception.Message)"
        return $null
    }
}

# Helper function to get the Target Framework Moniker (TFM)
function Get-TargetFramework {
    # Prefer TargetFrameworks (plural) if it exists, take the first one
    $targetFrameworks = Get-CsProjProperty -PropertyName "TargetFrameworks"
    if ($targetFrameworks) {
        $firstFramework = $targetFrameworks.Split(';')[0].Trim()
        Write-Verbose "Found TargetFrameworks, using first: $firstFramework"
        return $firstFramework
    }

    # Fallback to TargetFramework (singular)
    $targetFramework = Get-CsProjProperty -PropertyName "TargetFramework"
    if ($targetFramework) {
         Write-Verbose "Found TargetFramework: $targetFramework"
        return $targetFramework.Trim()
    }

    Write-Warning "Could not determine TargetFramework or TargetFrameworks from .csproj file."
    return $null # Indicate failure to find TFM
}

function Get-AssemblyVersion {
    param (
        [string]$configuration = "Release",
        [string]$runtime = ""
    )

    # --- Try to get version from .csproj file using helper ---
    $csprojVersion = Get-CsProjProperty -PropertyName "AssemblyVersion"
    if (-not [string]::IsNullOrEmpty($csprojVersion)) {
         if ($csprojVersion -match '^(\d+\.\d+\.\d+)\.(\d+)$') {
             $version = $matches[1]
             if ($matches[2] -ne "0") {
                 $version = "$version.$($matches[2])"
             }
             Write-Host "Found assembly version in .csproj file: $version"
             return $version
         } elseif ($csprojVersion -match '^(\d+\.\d+\.\d+)$') {
             # Handle 3-part version directly from csproj
             Write-Host "Found 3-part assembly version in .csproj file: $csprojVersion"
             return $csprojVersion
         } else {
              # Handle other formats if needed, or just return as is
              Write-Host "Found assembly version (unparsed) in .csproj file: $csprojVersion"
              return $csprojVersion
         }
    } else {
         # Optionally try FileVersion or Version properties as fallbacks from csproj
         $csprojFileVersion = Get-CsProjProperty -PropertyName "FileVersion"
         if (-not [string]::IsNullOrEmpty($csprojFileVersion)) {
             # Add parsing logic here if FileVersion format needs adjustment (e.g., matching AssemblyVersion format)
             if ($csprojFileVersion -match '^(\d+\.\d+\.\d+)\.(\d+)$') {
                 $version = $matches[1]
                 if ($matches[2] -ne "0") {
                     $version = "$version.$($matches[2])"
                 }
                 Write-Host "Found file version in .csproj file: $version"
                 return $version
             } elseif ($csprojFileVersion -match '^(\d+\.\d+\.\d+)$') {
                Write-Host "Found 3-part file version in .csproj file: $csprojFileVersion"
                return $csprojFileVersion
             } else {
                 Write-Host "Found file version (unparsed) in .csproj file: $csprojFileVersion"
                 return $csprojFileVersion
             }
         }
         # Add check for <Version> property if desired, applying similar parsing
         # $csprojPkgVersion = Get-CsProjProperty -PropertyName "Version" ...
    }

    Write-Host "Assembly/File Version not found directly in .csproj, checking generated AssemblyInfo.cs..."

    # --- Fallback to checking AssemblyInfo.cs ---

    # Get the TFM dynamically
    $targetFramework = Get-TargetFramework
    if (-not $targetFramework) {
        Write-Warning "Cannot determine Target Framework. Unable to find AssemblyInfo.cs path."
        Write-Warning "Returning default version 1.0.0"
        return "1.0.0" # Or handle error more severely if needed
    }

    # Construct the base path using the dynamic TFM
    $basePath = "obj\$configuration\$targetFramework"
    if ($runtime) {
        $basePath = Join-Path $basePath $runtime # Use Join-Path for robustness
    }

    # --- Attempt to find the AssemblyInfo.cs file using a pattern ---
    # Dynamically search for the generated file instead of using a hardcoded name
    $assemblyInfoSearchPath = Join-Path $basePath "*.AssemblyInfo.cs"
    Write-Verbose "Searching for AssemblyInfo file using pattern: $assemblyInfoSearchPath"

    # Find the first file matching the pattern in the target directory
    # Added -ErrorAction SilentlyContinue in case the path doesn't exist yet (e.g., before first build)
    $assemblyInfoFile = Get-ChildItem -Path $assemblyInfoSearchPath -File -ErrorAction SilentlyContinue | Select-Object -First 1

    # --- Check if the file was found and process it ---
    if ($assemblyInfoFile) {
        Write-Verbose "Found AssemblyInfo file: $($assemblyInfoFile.FullName)"
        # Use -Raw to read the whole file at once, potentially faster for smaller files
        $assemblyFileContent = Get-Content $assemblyInfoFile.FullName -Raw
		
		# First start with FileVersion. Ignore the last version digit if it's zero.
        $versionLine = $assemblyFileContent | Where-Object { $_ -match 'AssemblyFileVersionAttribute\("(.*)"\)' } | Select-Object -First 1
        if ($versionLine -match 'AssemblyFileVersionAttribute\("(\d+\.\d+\.\d+)\.(\d+)"\)') {
            $version = $matches[1]
            if ($matches[2] -ne "0") {
                $version = "$version.$($matches[2])"
            }
            Write-Host "Found assembly version in AssemblyFileVersionAttribute: $version"
            return $version
        }

        # Fall back to AssemblyVersionAttribute. Ignore the last version digit if it's zero.
        $versionLine = $assemblyFileContent | Where-Object { $_ -match 'AssemblyVersionAttribute\("(.*)"\)' } | Select-Object -First 1
        if ($versionLine -match 'AssemblyVersionAttribute\("(\d+\.\d+\.\d+)\.(\d+)"\)') {
            $version = $matches[1]
            if ($matches[2] -ne "0") {
                $version = "$version.$($matches[2])"
            }
            Write-Host "Found assembly version in AssemblyVersionAttribute: $version"
            return $version
        }

        # Fall back to AssemblyInformationalVersionAttribute
        $versionLine = $assemblyFileContent | Where-Object { $_ -match 'AssemblyInformationalVersionAttribute\("(.*)"\)' } | Select-Object -First 1
        # Updated regex to better handle versions like "1.2.3-beta+commitsha" -> extracts "1.2.3-beta"
        if ($versionLine -match 'AssemblyInformationalVersionAttribute\("([^"]+)"\)') {
             # Simpler extraction, might need refinement based on exact format needs
             $version = $matches[1]
             # Optional: remove build metadata if present (stuff after '+')
             $version = $version.Split('+')[0]
             Write-Host "Found assembly version in AssemblyInformationalVersionAttribute: $version"
             return $version
        }

        Write-Warning "Could not find recognized version attributes within $($assemblyInfoFile.Name)."

    } else {
        # File not found using the pattern
        Write-Warning "Couldn't find generated assembly info file (*.AssemblyInfo.cs) in expected path structure starting from: '$basePath'"
    }

    # Ultimate fallback if no version found anywhere
    Write-Warning "Couldn't find assembly version through .csproj or AssemblyInfo.cs. Using default 1.0.0"
    return "1.0.0"
}

# --- Main Script Logic ---

# Check if a .csproj file exists before proceeding
if (-not (Get-ChildItem -Filter "*.csproj" -File | Select-Object -First 1)) {
    Write-Error "No .csproj file found in the current directory. Script cannot continue."
    # Add Read-Host here if you want the error message to stay visible
    Read-Host "Press Enter to exit..."
    Exit 1 # Exit the script with an error code
}


# Create results directory
$resultsDir = "bin\Publish"
New-Item -ItemType Directory -Force -Path $resultsDir | Out-Null
Write-Host "Publishing results will be copied to: $($resultsDir)"

# --- Determine FileNameBase dynamically from csproj AssemblyName or default ---
$fileNameBase = Get-CsProjProperty -PropertyName "AssemblyName"
if ([string]::IsNullOrEmpty($fileNameBase)) {
    # Fallback: try to get it from the .csproj filename
    $csprojFile = Get-ChildItem -Filter "*.csproj" -File | Select-Object -First 1
    if ($csprojFile) {
        $fileNameBase = $csprojFile.BaseName
        Write-Warning "AssemblyName not found in .csproj. Using .csproj filename as base: $fileNameBase"
    } else {
        # Ultimate fallback if somehow csproj disappeared after initial check
        $fileNameBase = "ProjectNameUnknown"
        Write-Warning "Cannot determine AssemblyName or .csproj filename. Using default base: $fileNameBase"
    }
} else {
     Write-Host "Using AssemblyName from .csproj: $fileNameBase"
}
# --- End FileNameBase determination ---


# Get all publish profiles
$pubxmlFiles = Get-ChildItem -Path "Properties\PublishProfiles\*.pubxml" -File

if ($pubxmlFiles.Count -eq 0) {
    Write-Warning "No .pubxml files found in Properties\PublishProfiles\"
}

foreach ($pubxmlFile in $pubxmlFiles) {
    Write-Host "`n--------------------------------------------------"
    Write-Host "Processing publish profile: $($pubxmlFile.Name)"
    Write-Host "--------------------------------------------------"

    # Load and parse the pubxml file
    [xml]$pubxml = Get-Content $pubxmlFile.FullName
    $propertyGroup = $pubxml.Project.PropertyGroup

    # Extract properties from pubxml
    $publishDir = $propertyGroup.PublishDir
    $runtime = $propertyGroup.RuntimeIdentifier
    # Handle boolean conversion carefully, default to $false if missing/invalid
    $isSelfContained = $false
    if ($propertyGroup.SelfContained -ne $null -and $propertyGroup.SelfContained.ToLower() -eq 'true') {
         $isSelfContained = $true
    }

    # Validate essential properties
    if ([string]::IsNullOrEmpty($publishDir)) {
         Write-Warning "PublishDir not defined in $($pubxmlFile.Name). Skipping."
         continue
    }
     if ([string]::IsNullOrEmpty($runtime)) {
         Write-Warning "RuntimeIdentifier not defined in $($pubxmlFile.Name). Skipping."
         continue
    }


    # Determine architecture from runtime
    $architecture = if ($runtime -like "*-arm64*") { "ARM64" } elseif ($runtime -like "*-x64*") { "x64" } else { "x86" }

    # Determine type from SelfContained property
    $type = if ($isSelfContained) { "SelfContained" } else { "Framework" }

    # Get version for this specific configuration and runtime
    # Pass runtime only if it's relevant for finding AssemblyInfo.cs (which it is in our case)
    $version = Get-AssemblyVersion -configuration "Release" -runtime $runtime
    if ([string]::IsNullOrEmpty($version)) {
        Write-Warning "Failed to determine assembly version for profile $($pubxmlFile.Name). Skipping publish."
        continue # Skip this profile if version couldn't be found
    }

    # Sanitize version string for filename (replace potentially invalid chars like '+')
    $safeVersion = $version -replace '[^a-zA-Z0-9\.\-]', '_'

    # Construct the desired output assembly name
    $assemblyName = "{0}_{1}_{2}-{3}" -f $fileNameBase, $safeVersion, $architecture, $type

    # Run publish command
    Write-Host "Publishing '$($pubxmlFile.BaseName)' | Runtime: $runtime | Arch: $architecture | Type: $type | Version: $version | Output Assembly: $assemblyName"
    # Add /p:UseAppHost=true to ensure an .exe is generated, especially for framework-dependent console apps if needed. Often default for WinExe.
    # Consider adding /p:PublishSingleFile=true or /p:PublishTrimmed=true if desired based on profiles
    dotnet publish /p:PublishProfile=$($pubxmlFile.BaseName) /p:AssemblyName=$assemblyName /p:Configuration=Release # Ensure Release config is passed

    # Check the expected output path based on PublishDir and AssemblyName
    $sourceExe = Join-Path $publishDir "$assemblyName.exe"
    if (Test-Path $sourceExe) {
        Copy-Item $sourceExe -Destination $resultsDir -Force
        Write-Host "Successfully published and copied '$($assemblyName).exe' to $resultsDir"
    } else {
        # If .exe isn't found, maybe it's a DLL (e.g., class library)? Check for .dll
        $sourceDll = Join-Path $publishDir "$assemblyName.dll"
        if (Test-Path $sourceDll) {
             Copy-Item $sourceDll -Destination $resultsDir -Force
             Write-Host "Successfully published and copied '$($assemblyName).dll' to $resultsDir"
        } else {
             Write-Warning "Warning: Could not find published output '$($assemblyName).exe' or '$($assemblyName).dll' at expected location: $publishDir"
             Write-Warning "Check the build output and ensure the PublishDir '$publishDir' in '$($pubxmlFile.Name)' is correct relative to the project directory."
        }
    }
}

# List all files in the results directory
Write-Host "`n--------------------------------------------------"
Write-Host "Published outputs copied to '$resultsDir':"
$outputFiles = Get-ChildItem $resultsDir -File
if ($outputFiles) {
    $outputFiles | ForEach-Object {
        Write-Host " - $($_.Name)"
    }
} else {
    Write-Host " - No files found."
}
Write-Host "--------------------------------------------------"

# Keep the window open
Read-Host "Script finished. Press Enter to exit..."
# SIG # Begin signature block
# MII6fgYJKoZIhvcNAQcCoII6bzCCOmsCAQExDzANBglghkgBZQMEAgEFADB5Bgor
# BgEEAYI3AgEEoGswaTA0BgorBgEEAYI3AgEeMCYCAwEAAAQQH8w7YFlLCE63JNLG
# KX7zUQIBAAIBAAIBAAIBAAIBADAxMA0GCWCGSAFlAwQCAQUABCASj0cFzjSaOedi
# E8JgySGJVfy7JnbUw2I84NHUepgm6aCCIrQwggXMMIIDtKADAgECAhBUmNLR1FsZ
# lUgTecgRwIeZMA0GCSqGSIb3DQEBDAUAMHcxCzAJBgNVBAYTAlVTMR4wHAYDVQQK
# ExVNaWNyb3NvZnQgQ29ycG9yYXRpb24xSDBGBgNVBAMTP01pY3Jvc29mdCBJZGVu
# dGl0eSBWZXJpZmljYXRpb24gUm9vdCBDZXJ0aWZpY2F0ZSBBdXRob3JpdHkgMjAy
# MDAeFw0yMDA0MTYxODM2MTZaFw00NTA0MTYxODQ0NDBaMHcxCzAJBgNVBAYTAlVT
# MR4wHAYDVQQKExVNaWNyb3NvZnQgQ29ycG9yYXRpb24xSDBGBgNVBAMTP01pY3Jv
# c29mdCBJZGVudGl0eSBWZXJpZmljYXRpb24gUm9vdCBDZXJ0aWZpY2F0ZSBBdXRo
# b3JpdHkgMjAyMDCCAiIwDQYJKoZIhvcNAQEBBQADggIPADCCAgoCggIBALORKgeD
# Bmf9np3gx8C3pOZCBH8Ppttf+9Va10Wg+3cL8IDzpm1aTXlT2KCGhFdFIMeiVPvH
# or+Kx24186IVxC9O40qFlkkN/76Z2BT2vCcH7kKbK/ULkgbk/WkTZaiRcvKYhOuD
# PQ7k13ESSCHLDe32R0m3m/nJxxe2hE//uKya13NnSYXjhr03QNAlhtTetcJtYmrV
# qXi8LW9J+eVsFBT9FMfTZRY33stuvF4pjf1imxUs1gXmuYkyM6Nix9fWUmcIxC70
# ViueC4fM7Ke0pqrrBc0ZV6U6CwQnHJFnni1iLS8evtrAIMsEGcoz+4m+mOJyoHI1
# vnnhnINv5G0Xb5DzPQCGdTiO0OBJmrvb0/gwytVXiGhNctO/bX9x2P29Da6SZEi3
# W295JrXNm5UhhNHvDzI9e1eM80UHTHzgXhgONXaLbZ7LNnSrBfjgc10yVpRnlyUK
# xjU9lJfnwUSLgP3B+PR0GeUw9gb7IVc+BhyLaxWGJ0l7gpPKWeh1R+g/OPTHU3mg
# trTiXFHvvV84wRPmeAyVWi7FQFkozA8kwOy6CXcjmTimthzax7ogttc32H83rwjj
# O3HbbnMbfZlysOSGM1l0tRYAe1BtxoYT2v3EOYI9JACaYNq6lMAFUSw0rFCZE4e7
# swWAsk0wAly4JoNdtGNz764jlU9gKL431VulAgMBAAGjVDBSMA4GA1UdDwEB/wQE
# AwIBhjAPBgNVHRMBAf8EBTADAQH/MB0GA1UdDgQWBBTIftJqhSobyhmYBAcnz1AQ
# T2ioojAQBgkrBgEEAYI3FQEEAwIBADANBgkqhkiG9w0BAQwFAAOCAgEAr2rd5hnn
# LZRDGU7L6VCVZKUDkQKL4jaAOxWiUsIWGbZqWl10QzD0m/9gdAmxIR6QFm3FJI9c
# Zohj9E/MffISTEAQiwGf2qnIrvKVG8+dBetJPnSgaFvlVixlHIJ+U9pW2UYXeZJF
# xBA2CFIpF8svpvJ+1Gkkih6PsHMNzBxKq7Kq7aeRYwFkIqgyuH4yKLNncy2RtNwx
# AQv3Rwqm8ddK7VZgxCwIo3tAsLx0J1KH1r6I3TeKiW5niB31yV2g/rarOoDXGpc8
# FzYiQR6sTdWD5jw4vU8w6VSp07YEwzJ2YbuwGMUrGLPAgNW3lbBeUU0i/OxYqujY
# lLSlLu2S3ucYfCFX3VVj979tzR/SpncocMfiWzpbCNJbTsgAlrPhgzavhgplXHT2
# 6ux6anSg8Evu75SjrFDyh+3XOjCDyft9V77l4/hByuVkrrOj7FjshZrM77nq81YY
# uVxzmq/FdxeDWds3GhhyVKVB0rYjdaNDmuV3fJZ5t0GNv+zcgKCf0Xd1WF81E+Al
# GmcLfc4l+gcK5GEh2NQc5QfGNpn0ltDGFf5Ozdeui53bFv0ExpK91IjmqaOqu/dk
# ODtfzAzQNb50GQOmxapMomE2gj4d8yu8l13bS3g7LfU772Aj6PXsCyM2la+YZr9T
# 03u4aUoqlmZpxJTG9F9urJh4iIAGXKKy7aIwggbuMIIE1qADAgECAhMzAAM6+rLY
# O+J0xhAzAAAAAzr6MA0GCSqGSIb3DQEBDAUAMFoxCzAJBgNVBAYTAlVTMR4wHAYD
# VQQKExVNaWNyb3NvZnQgQ29ycG9yYXRpb24xKzApBgNVBAMTIk1pY3Jvc29mdCBJ
# RCBWZXJpZmllZCBDUyBBT0MgQ0EgMDIwHhcNMjUwNDA0MDkyOTA3WhcNMjUwNDA3
# MDkyOTA3WjBsMQswCQYDVQQGEwJVUzEQMA4GA1UECBMHV3lvbWluZzERMA8GA1UE
# BxMIU2hlcmlkYW4xGzAZBgNVBAoTElRoaW8gU29mdHdhcmUsIExMQzEbMBkGA1UE
# AxMSVGhpbyBTb2Z0d2FyZSwgTExDMIIBojANBgkqhkiG9w0BAQEFAAOCAY8AMIIB
# igKCAYEAjF+4pBrVReXqNTosoOEvMYf4ROcb1HM+m/Qa+OMWYBkilOM2InjKskvx
# GPc+gVjhGyiJcTj/1eNByLGgw3MUZSmhopAZgxx/BRv5VLOo8enN5k/WqRRE4bsI
# mauDWCK0Ob7aQC4He2hYXbfn8zBGMnK6CjyVv6UhkERGqirNn2eQOOAiSmWkXI0W
# fNIGg4hlMOm976mx0e+xfNb4/HUK7ycYq3lgHFUNNfxvmb92+HhTrRMTmX6K+usM
# Hnv3Fg9tVcI+r6bhbUZ5fD78WwB12zXjs6TM7joreMu2tPTYF5FyTEJvXsfLFxdm
# AkQqJq9Vtt/5jDDDOu0qDuXPjrHP5rBe1BkWj+ghojnB8uENaZPMdfSRSSN98dUh
# NWeBBgdn3S4CQQp3I6O2zRkCjBrM/3UrxyEXeNbWzR2TjOeNkJoEaqI4VhqLPd2d
# i/s4jNgxKmGsDQ75NfdyvUcWWwWDpLXjxCbRy59YfPNOfGtZwKKsByMnV9XQ4QSg
# CyoOLVwpAgMBAAGjggIZMIICFTAMBgNVHRMBAf8EAjAAMA4GA1UdDwEB/wQEAwIH
# gDA8BgNVHSUENTAzBgorBgEEAYI3YQEABggrBgEFBQcDAwYbKwYBBAGCN2GCyITf
# TKbD3mqByam9GIO77qlmMB0GA1UdDgQWBBScNdWEEWOxwsc7yhvD5PhF6bt32TAf
# BgNVHSMEGDAWgBQkRZmhd5AqfMPKg7BuZBaEKvgsZzBnBgNVHR8EYDBeMFygWqBY
# hlZodHRwOi8vd3d3Lm1pY3Jvc29mdC5jb20vcGtpb3BzL2NybC9NaWNyb3NvZnQl
# MjBJRCUyMFZlcmlmaWVkJTIwQ1MlMjBBT0MlMjBDQSUyMDAyLmNybDCBpQYIKwYB
# BQUHAQEEgZgwgZUwZAYIKwYBBQUHMAKGWGh0dHA6Ly93d3cubWljcm9zb2Z0LmNv
# bS9wa2lvcHMvY2VydHMvTWljcm9zb2Z0JTIwSUQlMjBWZXJpZmllZCUyMENTJTIw
# QU9DJTIwQ0ElMjAwMi5jcnQwLQYIKwYBBQUHMAGGIWh0dHA6Ly9vbmVvY3NwLm1p
# Y3Jvc29mdC5jb20vb2NzcDBmBgNVHSAEXzBdMFEGDCsGAQQBgjdMg30BATBBMD8G
# CCsGAQUFBwIBFjNodHRwOi8vd3d3Lm1pY3Jvc29mdC5jb20vcGtpb3BzL0RvY3Mv
# UmVwb3NpdG9yeS5odG0wCAYGZ4EMAQQBMA0GCSqGSIb3DQEBDAUAA4ICAQASZ0ru
# +MmmjD4EO43Wj70IxUj+lHqbve4AnQoJd2f0wiRQFY2TE/U4+XHNRc8AM+7QMsJd
# Dy/Mr1Axl4RLBPjeClEsGN7QOXeMFqMtZASDa2U0Jqt2agXF5hirA1TGeyP4izJ5
# OrMpNogFwRc0CcJxSx4dHf68qzjn1VdyJKoDKUQueb0xuNBb9brE4hMYCeu2CqqK
# Z011f8R90YZoCn737+/jZOaaNGDJLnmeBmAEiS8lomjb+Y9GjD+tcccD3a5jAC5Q
# pCuv6Pqxu7/ABcee/hFTQeCjEWJmg1J0owbZRon0phn4MqOqvuRwtecXwwdqnFFb
# 0aUKSriWPRDt5bPx594KGhihh6jlTfPcKKF515l20zE8wIEtUjIiBjMtIb6SbpOa
# ZbG+RIJ+cIzzVSuoMBXIprmfK7CuHJbiMzF3E91jv4EknerKRWjX9t2Fq1H794Ny
# Ua9gI0y1bucBH97n0uPq9dzaU34LmW40dbp96ihTMSyG6FmGTgKrzTIqA8vujyDg
# ge9moBTcootBt3jemfuy8JCnGqbQrX7OVuXRObj7F/vpnrgPRg+sktC5/DQAoJ7w
# 7G5Fhx6lhrYnhaZWghp+91Tt6OKGUtvoYgJ4jPuqsQM7epqTneNTzRDBDBgO0X7d
# oIXfMQ9EfIgEUsVLTW1PlJOI+9G3qXnUBcIvizCCBu4wggTWoAMCAQICEzMAAzr6
# stg74nTGEDMAAAADOvowDQYJKoZIhvcNAQEMBQAwWjELMAkGA1UEBhMCVVMxHjAc
# BgNVBAoTFU1pY3Jvc29mdCBDb3Jwb3JhdGlvbjErMCkGA1UEAxMiTWljcm9zb2Z0
# IElEIFZlcmlmaWVkIENTIEFPQyBDQSAwMjAeFw0yNTA0MDQwOTI5MDdaFw0yNTA0
# MDcwOTI5MDdaMGwxCzAJBgNVBAYTAlVTMRAwDgYDVQQIEwdXeW9taW5nMREwDwYD
# VQQHEwhTaGVyaWRhbjEbMBkGA1UEChMSVGhpbyBTb2Z0d2FyZSwgTExDMRswGQYD
# VQQDExJUaGlvIFNvZnR3YXJlLCBMTEMwggGiMA0GCSqGSIb3DQEBAQUAA4IBjwAw
# ggGKAoIBgQCMX7ikGtVF5eo1Oiyg4S8xh/hE5xvUcz6b9Br44xZgGSKU4zYieMqy
# S/EY9z6BWOEbKIlxOP/V40HIsaDDcxRlKaGikBmDHH8FG/lUs6jx6c3mT9apFETh
# uwiZq4NYIrQ5vtpALgd7aFhdt+fzMEYycroKPJW/pSGQREaqKs2fZ5A44CJKZaRc
# jRZ80gaDiGUw6b3vqbHR77F81vj8dQrvJxireWAcVQ01/G+Zv3b4eFOtExOZfor6
# 6wwee/cWD21Vwj6vpuFtRnl8PvxbAHXbNeOzpMzuOit4y7a09NgXkXJMQm9ex8sX
# F2YCRComr1W23/mMMMM67SoO5c+Osc/msF7UGRaP6CGiOcHy4Q1pk8x19JFJI33x
# 1SE1Z4EGB2fdLgJBCncjo7bNGQKMGsz/dSvHIRd41tbNHZOM542QmgRqojhWGos9
# 3Z2L+ziM2DEqYawNDvk193K9RxZbBYOktePEJtHLn1h88058a1nAoqwHIydX1dDh
# BKALKg4tXCkCAwEAAaOCAhkwggIVMAwGA1UdEwEB/wQCMAAwDgYDVR0PAQH/BAQD
# AgeAMDwGA1UdJQQ1MDMGCisGAQQBgjdhAQAGCCsGAQUFBwMDBhsrBgEEAYI3YYLI
# hN9MpsPeaoHJqb0Yg7vuqWYwHQYDVR0OBBYEFJw11YQRY7HCxzvKG8Pk+EXpu3fZ
# MB8GA1UdIwQYMBaAFCRFmaF3kCp8w8qDsG5kFoQq+CxnMGcGA1UdHwRgMF4wXKBa
# oFiGVmh0dHA6Ly93d3cubWljcm9zb2Z0LmNvbS9wa2lvcHMvY3JsL01pY3Jvc29m
# dCUyMElEJTIwVmVyaWZpZWQlMjBDUyUyMEFPQyUyMENBJTIwMDIuY3JsMIGlBggr
# BgEFBQcBAQSBmDCBlTBkBggrBgEFBQcwAoZYaHR0cDovL3d3dy5taWNyb3NvZnQu
# Y29tL3BraW9wcy9jZXJ0cy9NaWNyb3NvZnQlMjBJRCUyMFZlcmlmaWVkJTIwQ1Ml
# MjBBT0MlMjBDQSUyMDAyLmNydDAtBggrBgEFBQcwAYYhaHR0cDovL29uZW9jc3Au
# bWljcm9zb2Z0LmNvbS9vY3NwMGYGA1UdIARfMF0wUQYMKwYBBAGCN0yDfQEBMEEw
# PwYIKwYBBQUHAgEWM2h0dHA6Ly93d3cubWljcm9zb2Z0LmNvbS9wa2lvcHMvRG9j
# cy9SZXBvc2l0b3J5Lmh0bTAIBgZngQwBBAEwDQYJKoZIhvcNAQEMBQADggIBABJn
# Su74yaaMPgQ7jdaPvQjFSP6Uepu97gCdCgl3Z/TCJFAVjZMT9Tj5cc1FzwAz7tAy
# wl0PL8yvUDGXhEsE+N4KUSwY3tA5d4wWoy1kBINrZTQmq3ZqBcXmGKsDVMZ7I/iL
# Mnk6syk2iAXBFzQJwnFLHh0d/ryrOOfVV3IkqgMpRC55vTG40Fv1usTiExgJ67YK
# qopnTXV/xH3RhmgKfvfv7+Nk5po0YMkueZ4GYASJLyWiaNv5j0aMP61xxwPdrmMA
# LlCkK6/o+rG7v8AFx57+EVNB4KMRYmaDUnSjBtlGifSmGfgyo6q+5HC15xfDB2qc
# UVvRpQpKuJY9EO3ls/Hn3goaGKGHqOVN89wooXnXmXbTMTzAgS1SMiIGMy0hvpJu
# k5plsb5Egn5wjPNVK6gwFcimuZ8rsK4cluIzMXcT3WO/gSSd6spFaNf23YWrUfv3
# g3JRr2AjTLVu5wEf3ufS4+r13NpTfguZbjR1un3qKFMxLIboWYZOAqvNMioDy+6P
# IOCB72agFNyii0G3eN6Z+7LwkKcaptCtfs5W5dE5uPsX++meuA9GD6yS0Ln8NACg
# nvDsbkWHHqWGtieFplaCGn73VO3o4oZS2+hiAniM+6qxAzt6mpOd41PNEMEMGA7R
# ft2ghd8xD0R8iARSxUtNbU+Uk4j70bepedQFwi+LMIIHWjCCBUKgAwIBAgITMwAA
# AASWUEvS2+7LiAAAAAAABDANBgkqhkiG9w0BAQwFADBjMQswCQYDVQQGEwJVUzEe
# MBwGA1UEChMVTWljcm9zb2Z0IENvcnBvcmF0aW9uMTQwMgYDVQQDEytNaWNyb3Nv
# ZnQgSUQgVmVyaWZpZWQgQ29kZSBTaWduaW5nIFBDQSAyMDIxMB4XDTIxMDQxMzE3
# MzE1MloXDTI2MDQxMzE3MzE1MlowWjELMAkGA1UEBhMCVVMxHjAcBgNVBAoTFU1p
# Y3Jvc29mdCBDb3Jwb3JhdGlvbjErMCkGA1UEAxMiTWljcm9zb2Z0IElEIFZlcmlm
# aWVkIENTIEFPQyBDQSAwMjCCAiIwDQYJKoZIhvcNAQEBBQADggIPADCCAgoCggIB
# AOHOoOgzomOmwDsAj2wZUBdrY6N3JFGbmm+WaKzJ0aeKzpsGQ4k2yKcxZGf5PJOI
# rwSVdcOf2/6MpCPnlwKmmsTHcgDtDKHZxFuyJ30Pq05MpBMx8UWwjYOig7E52HP2
# HS+yCIiZYvJOdbqWhyy+wmJvWDXNEhWL5WhY9jtB4zvcvzUZnFjY2pmTpUY8VtnF
# oFLFHWs0h4EQnpPO1dmzP9e2/qPFl1FvdSKYIEWrJomeuVhBR1ym8oZti24QSumV
# pkKBXhPhlqylghiv6v+EYk2jDYR11r1r/v/yOfFLTsVYtw2itX0OmC8iCBh8w+Ap
# rXKxor8bqav3K6x7pxjQe//0JrpdmT/R3DpmP2qbYFJ8E/ttIPwN+4g37rlcOskt
# i6NP5Kf42/ifLxOBTKiIsMRgci+PNjzFQQt6nfzWxUGvDJo+np7FPhxKr/Wq/gG3
# CsLpm2aiSSpkKxmkjXVn5NjaHYHFjpqu48oW8cGTo5y49P28J7FDXDQHtPb/qoqM
# 8kEHrPAN1Fz3EUG/BvnNMmjtiAon1kyu8krslCfPJNZrTdtgjX7W44rYgHmn6GfV
# ZoZ+UX2/kvyuWq1b03C7pLeT3Uw0MZeeexCBOgPulxQaXbIzs5C83RIexC5PD1Tz
# I0HzwoCrSfOHNe33dgvfqcRdZREFBV2P2LQi/jZrPXFlAgMBAAGjggIOMIICCjAO
# BgNVHQ8BAf8EBAMCAYYwEAYJKwYBBAGCNxUBBAMCAQAwHQYDVR0OBBYEFCRFmaF3
# kCp8w8qDsG5kFoQq+CxnMFQGA1UdIARNMEswSQYEVR0gADBBMD8GCCsGAQUFBwIB
# FjNodHRwOi8vd3d3Lm1pY3Jvc29mdC5jb20vcGtpb3BzL0RvY3MvUmVwb3NpdG9y
# eS5odG0wGQYJKwYBBAGCNxQCBAweCgBTAHUAYgBDAEEwEgYDVR0TAQH/BAgwBgEB
# /wIBADAfBgNVHSMEGDAWgBTZQSmwDw9jbO9p1/XNKZ6kSGow5jBwBgNVHR8EaTBn
# MGWgY6Bhhl9odHRwOi8vd3d3Lm1pY3Jvc29mdC5jb20vcGtpb3BzL2NybC9NaWNy
# b3NvZnQlMjBJRCUyMFZlcmlmaWVkJTIwQ29kZSUyMFNpZ25pbmclMjBQQ0ElMjAy
# MDIxLmNybDCBrgYIKwYBBQUHAQEEgaEwgZ4wbQYIKwYBBQUHMAKGYWh0dHA6Ly93
# d3cubWljcm9zb2Z0LmNvbS9wa2lvcHMvY2VydHMvTWljcm9zb2Z0JTIwSUQlMjBW
# ZXJpZmllZCUyMENvZGUlMjBTaWduaW5nJTIwUENBJTIwMjAyMS5jcnQwLQYIKwYB
# BQUHMAGGIWh0dHA6Ly9vbmVvY3NwLm1pY3Jvc29mdC5jb20vb2NzcDANBgkqhkiG
# 9w0BAQwFAAOCAgEAZy04XZWzDSKJHSrc0mvIqPqRDveQnN1TsmP4ULCCHHTMpNoS
# Tsy7fzNVl30MhJQ5P0Lci81+t03Tm+SfpzvLdKc88Iu2WLzIjairwEDudLDDiZ90
# 94Qj6acTTYaBhVcc9lMokOG9rzq3LCyvUzhBV1m1DCTm0fTzNMGbAASIbuJOlVS8
# RA3tBknkF/2ROzx304OOC7n7eCCqmJp79QrqLKd4JRWLFXoC5zFmVGfFLTvRfEAo
# gKLiWIS+TpQpLIA2/b3vx0ISxZ3pX4OnULmyBbKgfSJQqJ2CiWfx2jGb2LQO8vRD
# kSuHMZb03rQlwB2soklx9LnhP0/dsFRtHLL+VXVMo+sla5ttr5SmAJFyDSrwzgfP
# rOIfk4EoZVGtgArthVp+yc5U0m6ZNCBPERLmJpLshPwU5JPd1gzMez8C55+CfuX5
# L2440NPDnsH6TIYfErj3UCqpmeNCOFtlMiSjDE23rdeiRYpkqgwoYJwgepcJaXtI
# H26Pe1O6a6W3wSqegdpNn+2Pk41q0GDfjnXDzskAHcRhjwcCUmiRt6IXZJQsYACe
# WpwsXmJe0o0ORLmumrYyHlYTdCnzyxT6WM+QkFPiQth+/ceHfzumDhUfWmHuePwh
# rqe3UVCHy0r9f49Az3OhJX92MlsZaFo/MnmN5B62RWgJUTMIQF8j0N6xF/cwggee
# MIIFhqADAgECAhMzAAAAB4ejNKN7pY4cAAAAAAAHMA0GCSqGSIb3DQEBDAUAMHcx
# CzAJBgNVBAYTAlVTMR4wHAYDVQQKExVNaWNyb3NvZnQgQ29ycG9yYXRpb24xSDBG
# BgNVBAMTP01pY3Jvc29mdCBJZGVudGl0eSBWZXJpZmljYXRpb24gUm9vdCBDZXJ0
# aWZpY2F0ZSBBdXRob3JpdHkgMjAyMDAeFw0yMTA0MDEyMDA1MjBaFw0zNjA0MDEy
# MDE1MjBaMGMxCzAJBgNVBAYTAlVTMR4wHAYDVQQKExVNaWNyb3NvZnQgQ29ycG9y
# YXRpb24xNDAyBgNVBAMTK01pY3Jvc29mdCBJRCBWZXJpZmllZCBDb2RlIFNpZ25p
# bmcgUENBIDIwMjEwggIiMA0GCSqGSIb3DQEBAQUAA4ICDwAwggIKAoICAQCy8MCv
# GYgo4t1UekxJbGkIVQm0Uv96SvjB6yUo92cXdylN65Xy96q2YpWCiTas7QPTkGnK
# 9QMKDXB2ygS27EAIQZyAd+M8X+dmw6SDtzSZXyGkxP8a8Hi6EO9Zcwh5A+wOALNQ
# bNO+iLvpgOnEM7GGB/wm5dYnMEOguua1OFfTUITVMIK8faxkP/4fPdEPCXYyy8NJ
# 1fmskNhW5HduNqPZB/NkWbB9xxMqowAeWvPgHtpzyD3PLGVOmRO4ka0WcsEZqyg6
# efk3JiV/TEX39uNVGjgbODZhzspHvKFNU2K5MYfmHh4H1qObU4JKEjKGsqqA6Rzi
# ybPqhvE74fEp4n1tiY9/ootdU0vPxRp4BGjQFq28nzawuvaCqUUF2PWxh+o5/TRC
# b/cHhcYU8Mr8fTiS15kRmwFFzdVPZ3+JV3s5MulIf3II5FXeghlAH9CvicPhhP+V
# aSFW3Da/azROdEm5sv+EUwhBrzqtxoYyE2wmuHKws00x4GGIx7NTWznOm6x/niqV
# i7a/mxnnMvQq8EMse0vwX2CfqM7Le/smbRtsEeOtbnJBbtLfoAsC3TdAOnBbUkbU
# fG78VRclsE7YDDBUbgWt75lDk53yi7C3n0WkHFU4EZ83i83abd9nHWCqfnYa9qIH
# PqjOiuAgSOf4+FRcguEBXlD9mAInS7b6V0UaNwIDAQABo4ICNTCCAjEwDgYDVR0P
# AQH/BAQDAgGGMBAGCSsGAQQBgjcVAQQDAgEAMB0GA1UdDgQWBBTZQSmwDw9jbO9p
# 1/XNKZ6kSGow5jBUBgNVHSAETTBLMEkGBFUdIAAwQTA/BggrBgEFBQcCARYzaHR0
# cDovL3d3dy5taWNyb3NvZnQuY29tL3BraW9wcy9Eb2NzL1JlcG9zaXRvcnkuaHRt
# MBkGCSsGAQQBgjcUAgQMHgoAUwB1AGIAQwBBMA8GA1UdEwEB/wQFMAMBAf8wHwYD
# VR0jBBgwFoAUyH7SaoUqG8oZmAQHJ89QEE9oqKIwgYQGA1UdHwR9MHsweaB3oHWG
# c2h0dHA6Ly93d3cubWljcm9zb2Z0LmNvbS9wa2lvcHMvY3JsL01pY3Jvc29mdCUy
# MElkZW50aXR5JTIwVmVyaWZpY2F0aW9uJTIwUm9vdCUyMENlcnRpZmljYXRlJTIw
# QXV0aG9yaXR5JTIwMjAyMC5jcmwwgcMGCCsGAQUFBwEBBIG2MIGzMIGBBggrBgEF
# BQcwAoZ1aHR0cDovL3d3dy5taWNyb3NvZnQuY29tL3BraW9wcy9jZXJ0cy9NaWNy
# b3NvZnQlMjBJZGVudGl0eSUyMFZlcmlmaWNhdGlvbiUyMFJvb3QlMjBDZXJ0aWZp
# Y2F0ZSUyMEF1dGhvcml0eSUyMDIwMjAuY3J0MC0GCCsGAQUFBzABhiFodHRwOi8v
# b25lb2NzcC5taWNyb3NvZnQuY29tL29jc3AwDQYJKoZIhvcNAQEMBQADggIBAH8l
# Kp7+1Kvq3WYK21cjTLpebJDjW4ZbOX3HD5ZiG84vjsFXT0OB+eb+1TiJ55ns0BHl
# uC6itMI2vnwc5wDW1ywdCq3TAmx0KWy7xulAP179qX6VSBNQkRXzReFyjvF2BGt6
# FvKFR/imR4CEESMAG8hSkPYso+GjlngM8JPn/ROUrTaeU/BRu/1RFESFVgK2wMz7
# fU4VTd8NXwGZBe/mFPZG6tWwkdmA/jLbp0kNUX7elxu2+HtHo0QO5gdiKF+YTYd1
# BGrmNG8sTURvn09jAhIUJfYNotn7OlThtfQjXqe0qrimgY4Vpoq2MgDW9ESUi1o4
# pzC1zTgIGtdJ/IvY6nqa80jFOTg5qzAiRNdsUvzVkoYP7bi4wLCj+ks2GftUct+f
# GUxXMdBUv5sdr0qFPLPB0b8vq516slCfRwaktAxK1S40MCvFbbAXXpAZnU20FaAo
# Dwqq/jwzwd8Wo2J83r7O3onQbDO9TyDStgaBNlHzMMQgl95nHBYMelLEHkUnVVVT
# UsgC0Huj09duNfMaJ9ogxhPNThgq3i8w3DAGZ61AMeF0C1M+mU5eucj1Ijod5O2M
# MPeJQ3/vKBtqGZg4eTtUHt/BPjN74SsJsyHqAdXVS5c+ItyKWg3Eforhox9k3Wgt
# WTpgV4gkSiS4+A09roSdOI4vrRw+p+fL4WrxSK5nMYIXIDCCFxwCAQEwcTBaMQsw
# CQYDVQQGEwJVUzEeMBwGA1UEChMVTWljcm9zb2Z0IENvcnBvcmF0aW9uMSswKQYD
# VQQDEyJNaWNyb3NvZnQgSUQgVmVyaWZpZWQgQ1MgQU9DIENBIDAyAhMzAAM6+rLY
# O+J0xhAzAAAAAzr6MA0GCWCGSAFlAwQCAQUAoF4wEAYKKwYBBAGCNwIBDDECMAAw
# GQYJKoZIhvcNAQkDMQwGCisGAQQBgjcCAQQwLwYJKoZIhvcNAQkEMSIEIPadbYj8
# vbQliqSGprdzwpJKJr22+TXZ2m7282sfO/yyMA0GCSqGSIb3DQEBAQUABIIBgBmI
# 1fY5yuc3KjGxw9XjASfHy8uEaXeoBR8F6VQ1DBSSwqWZsG5/Y/3bvyhi9EimrBX0
# c+8OCETBJ+wVy47LCo9comhmgEe3WRiGxDr2L8KAyZ7nH03nXASj7q5etYjy5m3F
# UZYZD7ZYvdpE9/1NF1WA7LKwFRHidnpOLRNsk1RGyfwnNNZ5VZyVqIGrLRdsu2Rc
# DW86kUYt3Ug3SMKw6CFSfLOHBGq4TBdFUlTOv5yEJ9Vy378nLbltROYqtqSRe7lq
# EHQezFz9najVdjWyWmQTAkxA/mAe4ioOM/yfR9Xqo1R1ToeQB5bTwMBo8UVrnjVm
# h5NQdsFEN7R66SLOMSH0/+Xmq18QEVZXdZA32aUBxVgeo8NvvVoQxJ2BDzGBM64h
# 5l2oG7PdU1rHt1GH4kwCgfoNd02OzqFupexSSfP4swF1dGmF2KwPNa6AKz0IErz8
# JwbEkp5CxpWfcdgoZAazkZdrm5tJ8x0B9Xa15j+JGP4PaapuVzjh0YG4Xjt5vKGC
# FKAwghScBgorBgEEAYI3AwMBMYIUjDCCFIgGCSqGSIb3DQEHAqCCFHkwghR1AgED
# MQ8wDQYJYIZIAWUDBAIBBQAwggFhBgsqhkiG9w0BCRABBKCCAVAEggFMMIIBSAIB
# AQYKKwYBBAGEWQoDATAxMA0GCWCGSAFlAwQCAQUABCBFtwqg6aaEcQ0Z6XtruNxN
# t4zft8zn6QGjE9BUGvqARAIGZ+bp8BM2GBMyMDI1MDQwNDE4NTQyMS4zMzhaMASA
# AgH0oIHgpIHdMIHaMQswCQYDVQQGEwJVUzETMBEGA1UECBMKV2FzaGluZ3RvbjEQ
# MA4GA1UEBxMHUmVkbW9uZDEeMBwGA1UEChMVTWljcm9zb2Z0IENvcnBvcmF0aW9u
# MSUwIwYDVQQLExxNaWNyb3NvZnQgQW1lcmljYSBPcGVyYXRpb25zMSYwJAYDVQQL
# Ex1UaGFsZXMgVFNTIEVTTjpCQjczLTk2RkQtNzdFRjE1MDMGA1UEAxMsTWljcm9z
# b2Z0IFB1YmxpYyBSU0EgVGltZSBTdGFtcGluZyBBdXRob3JpdHmggg8gMIIHgjCC
# BWqgAwIBAgITMwAAAAXlzw//Zi7JhwAAAAAABTANBgkqhkiG9w0BAQwFADB3MQsw
# CQYDVQQGEwJVUzEeMBwGA1UEChMVTWljcm9zb2Z0IENvcnBvcmF0aW9uMUgwRgYD
# VQQDEz9NaWNyb3NvZnQgSWRlbnRpdHkgVmVyaWZpY2F0aW9uIFJvb3QgQ2VydGlm
# aWNhdGUgQXV0aG9yaXR5IDIwMjAwHhcNMjAxMTE5MjAzMjMxWhcNMzUxMTE5MjA0
# MjMxWjBhMQswCQYDVQQGEwJVUzEeMBwGA1UEChMVTWljcm9zb2Z0IENvcnBvcmF0
# aW9uMTIwMAYDVQQDEylNaWNyb3NvZnQgUHVibGljIFJTQSBUaW1lc3RhbXBpbmcg
# Q0EgMjAyMDCCAiIwDQYJKoZIhvcNAQEBBQADggIPADCCAgoCggIBAJ5851Jj/eDF
# nwV9Y7UGIqMcHtfnlzPREwW9ZUZHd5HBXXBvf7KrQ5cMSqFSHGqg2/qJhYqOQxwu
# EQXG8kB41wsDJP5d0zmLYKAY8Zxv3lYkuLDsfMuIEqvGYOPURAH+Ybl4SJEESnt0
# MbPEoKdNihwM5xGv0rGofJ1qOYSTNcc55EbBT7uq3wx3mXhtVmtcCEr5ZKTkKKE1
# CxZvNPWdGWJUPC6e4uRfWHIhZcgCsJ+sozf5EeH5KrlFnxpjKKTavwfFP6XaGZGW
# UG8TZaiTogRoAlqcevbiqioUz1Yt4FRK53P6ovnUfANjIgM9JDdJ4e0qiDRm5sOT
# iEQtBLGd9Vhd1MadxoGcHrRCsS5rO9yhv2fjJHrmlQ0EIXmp4DhDBieKUGR+eZ4C
# NE3ctW4uvSDQVeSp9h1SaPV8UWEfyTxgGjOsRpeexIveR1MPTVf7gt8hY64XNPO6
# iyUGsEgt8c2PxF87E+CO7A28TpjNq5eLiiunhKbq0XbjkNoU5JhtYUrlmAbpxRjb
# 9tSreDdtACpm3rkpxp7AQndnI0Shu/fk1/rE3oWsDqMX3jjv40e8KN5YsJBnczyW
# B4JyeeFMW3JBfdeAKhzohFe8U5w9WuvcP1E8cIxLoKSDzCCBOu0hWdjzKNu8Y5Sw
# B1lt5dQhABYyzR3dxEO/T1K/BVF3rV69AgMBAAGjggIbMIICFzAOBgNVHQ8BAf8E
# BAMCAYYwEAYJKwYBBAGCNxUBBAMCAQAwHQYDVR0OBBYEFGtpKDo1L0hjQM972K9J
# 6T7ZPdshMFQGA1UdIARNMEswSQYEVR0gADBBMD8GCCsGAQUFBwIBFjNodHRwOi8v
# d3d3Lm1pY3Jvc29mdC5jb20vcGtpb3BzL0RvY3MvUmVwb3NpdG9yeS5odG0wEwYD
# VR0lBAwwCgYIKwYBBQUHAwgwGQYJKwYBBAGCNxQCBAweCgBTAHUAYgBDAEEwDwYD
# VR0TAQH/BAUwAwEB/zAfBgNVHSMEGDAWgBTIftJqhSobyhmYBAcnz1AQT2ioojCB
# hAYDVR0fBH0wezB5oHegdYZzaHR0cDovL3d3dy5taWNyb3NvZnQuY29tL3BraW9w
# cy9jcmwvTWljcm9zb2Z0JTIwSWRlbnRpdHklMjBWZXJpZmljYXRpb24lMjBSb290
# JTIwQ2VydGlmaWNhdGUlMjBBdXRob3JpdHklMjAyMDIwLmNybDCBlAYIKwYBBQUH
# AQEEgYcwgYQwgYEGCCsGAQUFBzAChnVodHRwOi8vd3d3Lm1pY3Jvc29mdC5jb20v
# cGtpb3BzL2NlcnRzL01pY3Jvc29mdCUyMElkZW50aXR5JTIwVmVyaWZpY2F0aW9u
# JTIwUm9vdCUyMENlcnRpZmljYXRlJTIwQXV0aG9yaXR5JTIwMjAyMC5jcnQwDQYJ
# KoZIhvcNAQEMBQADggIBAF+Idsd+bbVaFXXnTHho+k7h2ESZJRWluLE0Oa/pO+4g
# e/XEizXvhs0Y7+KVYyb4nHlugBesnFqBGEdC2IWmtKMyS1OWIviwpnK3aL5Jedwz
# beBF7POyg6IGG/XhhJ3UqWeWTO+Czb1c2NP5zyEh89F72u9UIw+IfvM9lzDmc2O2
# END7MPnrcjWdQnrLn1Ntday7JSyrDvBdmgbNnCKNZPmhzoa8PccOiQljjTW6GePe
# 5sGFuRHzdFt8y+bN2neF7Zu8hTO1I64XNGqst8S+w+RUdie8fXC1jKu3m9KGIqF4
# aldrYBamyh3g4nJPj/LR2CBaLyD+2BuGZCVmoNR/dSpRCxlot0i79dKOChmoONqb
# MI8m04uLaEHAv4qwKHQ1vBzbV/nG89LDKbRSSvijmwJwxRxLLpMQ/u4xXxFfR4f/
# gksSkbJp7oqLwliDm/h+w0aJ/U5ccnYhYb7vPKNMN+SZDWycU5ODIRfyoGl59BsX
# R/HpRGtiJquOYGmvA/pk5vC1lcnbeMrcWD/26ozePQ/TWfNXKBOmkFpvPE8CH+Ee
# GGWzqTCjdAsno2jzTeNSxlx3glDGJgcdz5D/AAxw9Sdgq/+rY7jjgs7X6fqPTXPm
# aCAJKVHAP19oEjJIBwD1LyHbaEgBxFCogYSOiUIr0Xqcr1nJfiWG2GwYe6ZoAF1b
# MIIHljCCBX6gAwIBAgITMwAAAEXfe+fnDAkWngAAAAAARTANBgkqhkiG9w0BAQwF
# ADBhMQswCQYDVQQGEwJVUzEeMBwGA1UEChMVTWljcm9zb2Z0IENvcnBvcmF0aW9u
# MTIwMAYDVQQDEylNaWNyb3NvZnQgUHVibGljIFJTQSBUaW1lc3RhbXBpbmcgQ0Eg
# MjAyMDAeFw0yNDExMjYxODQ4NDdaFw0yNTExMTkxODQ4NDdaMIHaMQswCQYDVQQG
# EwJVUzETMBEGA1UECBMKV2FzaGluZ3RvbjEQMA4GA1UEBxMHUmVkbW9uZDEeMBwG
# A1UEChMVTWljcm9zb2Z0IENvcnBvcmF0aW9uMSUwIwYDVQQLExxNaWNyb3NvZnQg
# QW1lcmljYSBPcGVyYXRpb25zMSYwJAYDVQQLEx1UaGFsZXMgVFNTIEVTTjpCQjcz
# LTk2RkQtNzdFRjE1MDMGA1UEAxMsTWljcm9zb2Z0IFB1YmxpYyBSU0EgVGltZSBT
# dGFtcGluZyBBdXRob3JpdHkwggIiMA0GCSqGSIb3DQEBAQUAA4ICDwAwggIKAoIC
# AQDAjtP0N0JgNSdh+Pi9r4yT210+bHbdwvCUccgDxkQi5MSCsVXwXmAgAcPO+B2s
# uloB81i3nL5W2nHlEdsVUmdGCfBYTcWsMoY7Wv6QVdxdELHNqNvuu/uf6kFLCHDA
# qZB6JMDxRk26OiwtDVbSiM4QvvExziXmbMu6ADoIvrXzAvuBplbBo4arpFE4Lti/
# WvXz7LU7aZKgQzMzeVWvc+8iPdROa1ui9F5k5zs2U+4Y9MDCNe2qlLXoZTsN/gKs
# G8L1rmf0zXmioK1aNkRmWyB8zMDbwq9IqqpL9TJEFTBssQLSQ/p3+s7PLLS7EKA/
# bn2e3NQXpz43teYLlfTg8Wjs5KcpywnTTiP1biCSLoy1EcGU9bWEHcRSU/Mx/Hu8
# 9WT7/R6uHcMp7lRSJnnhoLqFyWTepzvg6hFxeRGKqF4Tt8MsyaQbMbOIx+KLyjUr
# R9wNSEvUS19/YYvobQ3eqz/ay0mu2bijKhRElrCVM3nInznPNwXVdJozs/n3mOEX
# PyAHhAFO+zrvBBrmeswlEc1ZOW+phsiahhhfvKHOYBQsU7d6yyeu8iuIamLWm/g2
# +g9Ky+ChDvQONVSsNuJ/yDA6Uh5+ly6dsZjMIo1kLes57FTokZ5TQ2VksD1Q9oXe
# nF6eMQWqxlZWvckp/r+xuy0AgWzIzZk4yK+Ujyl9pZLhbwIDAQABo4IByzCCAccw
# HQYDVR0OBBYEFCXQ2+r1JdBEyafwHcavPYdjK5XyMB8GA1UdIwQYMBaAFGtpKDo1
# L0hjQM972K9J6T7ZPdshMGwGA1UdHwRlMGMwYaBfoF2GW2h0dHA6Ly93d3cubWlj
# cm9zb2Z0LmNvbS9wa2lvcHMvY3JsL01pY3Jvc29mdCUyMFB1YmxpYyUyMFJTQSUy
# MFRpbWVzdGFtcGluZyUyMENBJTIwMjAyMC5jcmwweQYIKwYBBQUHAQEEbTBrMGkG
# CCsGAQUFBzAChl1odHRwOi8vd3d3Lm1pY3Jvc29mdC5jb20vcGtpb3BzL2NlcnRz
# L01pY3Jvc29mdCUyMFB1YmxpYyUyMFJTQSUyMFRpbWVzdGFtcGluZyUyMENBJTIw
# MjAyMC5jcnQwDAYDVR0TAQH/BAIwADAWBgNVHSUBAf8EDDAKBggrBgEFBQcDCDAO
# BgNVHQ8BAf8EBAMCB4AwZgYDVR0gBF8wXTBRBgwrBgEEAYI3TIN9AQEwQTA/Bggr
# BgEFBQcCARYzaHR0cDovL3d3dy5taWNyb3NvZnQuY29tL3BraW9wcy9Eb2NzL1Jl
# cG9zaXRvcnkuaHRtMAgGBmeBDAEEAjANBgkqhkiG9w0BAQwFAAOCAgEAbL2151p4
# JPix4UcYsqBC15GI/LS3A22guo5TzSBZrOiLQvkMdAaFeJUMxUlv3O7UhTcnm6c3
# HBp32EDr/iN67+QeBkXPcQSzNzNPjzSfPDHr3Na1U+4If/9vuHWo1nSntgqlqZQO
# 7VmMFa5KaA+Er8aBUtcs7VNDqe2uNvPxswl/fkexQVa8JGts+lfiGE16lRsvSNTe
# XVgQIeiV4OG2uepXm/6vP+VdDGEbJVKM+H41ODzRfCTw5//uxpie8x1bbzGh6VQs
# hicWpPE+f7W8olfVCeUfMEFS1YpUM9T98wRFxTZQXTnZyGKfRMJBrI/xwAF3WNhg
# gtAxq2JgnNIsAB02I8zH9yWmgPTDXd6CkwP0HdvHSrKu7PYxArfUpHhnCgoYt8LC
# 65rLMHZoQNyndD5JUaCeBifxJpOOd5RkuiGH9aT0Dgs6RmEMFiMVDUGi1tNz0phR
# XTklc1qF4xLBGIVC4J5mdO2rCE35SlM76VkbMGPE9hNc5tBitjq+wHiPOaDNXb5S
# QTonadzMk/ot2KMavY8o+lDdtUbx2mqc5pjxqEq1Ci6hN8k8cbbkGutfbCE9yHzF
# kFhJafQ1iP/JkqN79yuoli9SgQAuGiZBu4FTn4W/hT9HzHMxQYmCh4ciHyf06g03
# YkS0w57KkjqhOsZVG0pi0fJmZvhrmBQzxNcxggPUMIID0AIBATB4MGExCzAJBgNV
# BAYTAlVTMR4wHAYDVQQKExVNaWNyb3NvZnQgQ29ycG9yYXRpb24xMjAwBgNVBAMT
# KU1pY3Jvc29mdCBQdWJsaWMgUlNBIFRpbWVzdGFtcGluZyBDQSAyMDIwAhMzAAAA
# Rd975+cMCRaeAAAAAABFMA0GCWCGSAFlAwQCAQUAoIIBLTAaBgkqhkiG9w0BCQMx
# DQYLKoZIhvcNAQkQAQQwLwYJKoZIhvcNAQkEMSIEIDXtI7TpQURnzH3YKsf1AO3f
# pWMCJd5WD2GI/7a0S1HnMIHdBgsqhkiG9w0BCRACLzGBzTCByjCBxzCBoAQguARV
# OsjIij9x4y/muE8czvSIz0XS68pBFQ5+Id/SbnEwfDBlpGMwYTELMAkGA1UEBhMC
# VVMxHjAcBgNVBAoTFU1pY3Jvc29mdCBDb3Jwb3JhdGlvbjEyMDAGA1UEAxMpTWlj
# cm9zb2Z0IFB1YmxpYyBSU0EgVGltZXN0YW1waW5nIENBIDIwMjACEzMAAABF33vn
# 5wwJFp4AAAAAAEUwIgQgXsFeUvl2/QP8n8lYrnQNmVY7PxTPJSmCUXKBDAkfTcYw
# DQYJKoZIhvcNAQELBQAEggIApV+qVSP/AnzqoGQRMV1Z/KSPBXd05xEK2TSI63xX
# yTWRVlRpeQ/oMYQnAmJNtv8+7OnkitAlAH9kNLxtSx9OnbzYj29SC2RS7yCoQeQ2
# 2Z4QpEBx7VXpC9p4J112c4/YtDnB8RKFp4LbvMjeeEuDxvRp/MGZ0oT3ufVmr0TW
# DaJvF5OM3LnY8d2r2LoRRkBpBKK+SilqxtWBsC40HAvt0W+SgOEan24BAIozFfpi
# vF9WUq4KZLYeFSlBPjnOh9O0mkAt8yemCyQTlBmp1xSOTw49SeGau2JdDNVw8qKs
# vz4T+hRAUhF05aQK8srsNVHfNFdCJszo8tkSJBsBOPgEUWR5XW1SGYC6ZcxHE38r
# RYVWwrRqPdOF7sGcr1eypvFe4vSGbfwlBCAlFxXMO8J/vZiAwXCiHrhxJItrU1D4
# q2nadMYQd0W7oZ6uEJj2P8ER3J0FcZOX9k8W+pRZnxdFjCx/2oCGEwM1qjoTZB6z
# qOGhfhb31DkOIvdVUe8MxScMbBSfS30Tru8I13Y8sHulmfqgr1KxdXGY3CEFvI5Y
# TSU/pmrXgCE+UreZndNIPRTZ1/G5nYFSVRBve7th+5fst+XtSknRZCfrHtIYzLSQ
# CIwkJ+b+M9ePcHaH908uyFe/WBnX3z5dzfS8ypRkExqvn8X0vDdARYGhuv5+Y8eQ
# UUE=
# SIG # End signature block
