# PowerShell script to run all publishing profiles for a Visual Studio .NET project. Also automatically renames resulting binary file to include version number.
# Author: ThioJoe (https://github.com/ThioJoe)

# VERSION: 1.2.0 (Updated 4/4/25)

# --------------------------------------------------------------

# Tip 1: Make this script easier to run by adding it as an "External Tool" so it will show up in the "Tools" dropdown menu.
#
#     You can do this by:
#          1. Going to Tools > External Tools > Add
#          2. For 'Command' put 'powershell.exe'
#          3. For 'Arguments' use the -File parameter with the relative path (relative to project directory) and filename you have this script, for example:
#  	  		-File "Resources\publish-all.ps1"
#          4. Be sure to set the "Initial Directory" box to:
#            $(ProjectDir)
#
# Tip 2: You can pass built in variables in the Arguments box.
#
#     For example to set the file name base to the project target name:
#          -File "Resources\publish-all.ps1" -FileNameBase $(TargetName)
#
# --------------------------------------------------------------

param(
    [switch]$AddVersion,        # Switch: Adds the version number to the filename (defaulting to FileVersion, then AssemblyVersion, then Assembly Version Info)
	[switch]$AddRuntime,        # Switch to add FULL runtime ID + Type (e.g., _linux-x64-SelfContained)
	[switch]$AddArchitecture,   # Switch to add ONLY architecture (e.g., _x64)
    [string]$FileNameBase,      # String: Set the base name of the file instead of defaulting to Assembly Name
	[object]$AddType = $null    # Switch to add deployment Type (e.g., -SelfContained). Also accepts "auto" to only add the type if there are multiple for the same runtime

)

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

# --- Determine FileNameBase ---
# Check if the FileNameBase parameter was provided via command line and has a value
if (-not [string]::IsNullOrEmpty($FileNameBase)) {
    # If provided via parameter, the variable $FileNameBase is already set by PowerShell. Just log it.
    Write-Host "Using FileNameBase provided via parameter: $FileNameBase"
} else {
    # Parameter was NOT provided or was empty, determine dynamically from csproj AssemblyName or default
    Write-Verbose "FileNameBase parameter not provided or empty. Attempting to determine from .csproj..."
    $fileNameBase = Get-CsProjProperty -PropertyName "AssemblyName" # This assigns to the $fileNameBase variable only if the parameter wasn't used
    if ([string]::IsNullOrEmpty($fileNameBase)) {
        # Fallback: try to get it from the .csproj filename
        $csprojFile = Get-ChildItem -Filter "*.csproj" -File | Select-Object -First 1
        if ($csprojFile) {
            $fileNameBase = $csprojFile.BaseName # Assign the fallback value
            Write-Warning "AssemblyName not found in .csproj. Using .csproj filename as base: $fileNameBase"
        } else {
            # Ultimate fallback if somehow csproj disappeared after initial check
            $fileNameBase = "ProjectNameUnknown" # Assign the ultimate fallback value
            Write-Warning "Cannot determine AssemblyName or .csproj filename. Using default base: $fileNameBase"
        }
    } else {
         Write-Host "Using AssemblyName from .csproj: $fileNameBase"
    }
}
# --- End FileNameBase determination ---


# Get all publish profiles
$pubxmlFiles = Get-ChildItem -Path "Properties\PublishProfiles\*.pubxml" -File

if ($pubxmlFiles.Count -eq 0) {
    Write-Warning "No .pubxml files found in Properties\PublishProfiles\"
}

# --- Pre-process profiles to count runtimes (for AddType="auto") ---
$runtimeCounts = @{}
if ($AddType -is [string] -and $AddType -eq 'auto') {
    Write-Verbose "AddType='auto' detected. Pre-calculating runtime counts."
    foreach ($file in $pubxmlFiles) {
        try {
            [xml]$xmlContent = Get-Content $file.FullName
            # Check if PropertyGroup and RuntimeIdentifier exist before accessing
            $profileRuntimeNode = $xmlContent.SelectSingleNode("/Project/PropertyGroup/RuntimeIdentifier")
            if ($null -ne $profileRuntimeNode) {
                $profileRuntime = $profileRuntimeNode.'#text'.Trim()
                if (-not [string]::IsNullOrEmpty($profileRuntime)) {
                    if ($runtimeCounts.ContainsKey($profileRuntime)) {
                        $runtimeCounts[$profileRuntime]++
                    } else {
                        $runtimeCounts[$profileRuntime] = 1
                    }
                } else {
                     Write-Verbose "Profile '$($file.Name)' has an empty RuntimeIdentifier. Skipping for count."
                }
            } else {
                Write-Verbose "Skipping profile '$($file.Name)' for runtime count: RuntimeIdentifier node not found."
            }
        } catch {
            Write-Warning "Error processing '$($file.Name)' for runtime count: $($_.Exception.Message)"
        }
    }
    # You can uncomment the next line for debugging if needed
    # Write-Host "DEBUG: Runtime counts calculated: $($runtimeCounts | Out-String)"
}
# --- End Pre-processing ---

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

    # Construct the desired output assembly name based on parameters
	# --- Initialize filename components ---
	$versionPart = ""
	$runtimePart = ""
	$logVersion = "(without version)"
	$logRuntime = "(without runtime)"

	# --- Populate Version Component (if requested) ---
	if ($AddVersion) {
		# Note: $version should have been retrieved earlier, before the original block you're replacing.
		# If $version retrieval failed earlier, the script should have already skipped via 'continue'.
		if (-not [string]::IsNullOrEmpty($version)) {
			$safeVersion = $version -replace '[^a-zA-Z0-9\.\-]', '_'
			$versionPart = "_$safeVersion" # Set the version part including separator
			$logVersion = "(with version $version)"
		} else {
			 # This case should ideally not be reached if the earlier check worked, but as a safeguard:
			 Write-Warning "Version was requested but could not be determined for '$($pubxmlFile.Name)'. Version part will be missing."
		}
	}

	# --- Populate Runtime/Architecture Component (based on switches) ---
	$runtimeOrArchPart = "_$safeRuntime"               # Holds the string to append (e.g., "_linux-x64-SelfContained" or "_x64")
	$logRuntimeOrArch = "(with runtime $safeRuntime)" # Log message part

	if ($AddRuntime) { # Add Full Runtime + Type (takes precedence)
		# $runtime is the full ID from pubxml (e.g., "linux-x64", "osx-arm64", "win-x64")
		# $isSelfContained is boolean from pubxml
		$type = if ($isSelfContained) { "SelfContained" } else { "Framework" }
		# Sanitize the runtime string if needed (hyphens are generally safe)
		$safeRuntime = $runtime # Or add: -replace '[^a-zA-Z0-9\-\.]', '_'
		if (-not [string]::IsNullOrEmpty($safeRuntime)) {
			 $runtimeOrArchPart = "_$safeRuntime"
			 $logRuntimeOrArch = "(with runtime $safeRuntime)"
		} else {
			 Write-Warning "Runtime info was requested (-AddRuntime) but runtime ID from profile '$($pubxmlFile.Name)' was empty. Runtime part will be missing."
		}

	} elseif ($AddArchitecture) { # Add Architecture ONLY (if -AddRuntime wasn't used)
		 # $runtime is the full ID from pubxml
		 $architecture = if ($runtime -like "*-arm64*") { "ARM64" } elseif ($runtime -like "*-x64*") { "x64" } elseif ($runtime -like "*-x86*") { "x86" } else { $null } # Added x86 explicitly and default null
		 if (-not [string]::IsNullOrEmpty($architecture)) {
			$runtimeOrArchPart = "_$architecture"
			$logRuntimeOrArch = "(with architecture $architecture)"
		 } else {
			Write-Warning "Architecture info was requested (-AddArchitecture) but could not be determined from runtime ID '$runtime' in profile '$($pubxmlFile.Name)'. Architecture part will be missing."
		 }
	}
	
	# --- Populate Type Component (if requested based on $AddType mode) ---
	$typePart = ""
	$logType = "(without type info)"
	$addTypeFlag = $false # Flag to indicate if type should be added for this profile

	# Determine the type string based on SelfContained property (needed for all modes)
	# $isSelfContained is derived earlier in the loop from the pubxml
	$type = if ($isSelfContained) { "SelfContained" } else { "Framework" }

	# Check the state of the $AddType parameter
	if ($AddType -is [System.Management.Automation.SwitchParameter] -and $AddType.IsPresent) {
		# Mode 1: -AddType used as a switch - always add type
		$addTypeFlag = $true
		Write-Verbose "AddType switch detected. Adding type '$type'."

	} elseif ($AddType -is [string] -and $AddType -eq 'auto') {
		# Mode 2: -AddType "auto" - add type only if runtime is duplicated
		# $runtime is derived earlier in the loop from the pubxml
		Write-Verbose "AddType='auto' detected. Checking runtime '$runtime' count."
		if ($runtimeCounts.ContainsKey($runtime) -and $runtimeCounts[$runtime] -gt 1) {
			$addTypeFlag = $true
			Write-Verbose "Runtime '$runtime' has count $($runtimeCounts[$runtime]) (>1). Adding type '$type'."
		} else {
			$countInfo = if ($runtimeCounts.ContainsKey($runtime)) { $runtimeCounts[$runtime] } else { 0 }
			Write-Verbose "Runtime '$runtime' count is $countInfo (not >1). Not adding type."
		}

	} elseif ($AddType -is [string] -and -not [string]::IsNullOrEmpty($AddType) -and $AddType -ne 'auto') {
		# Handle invalid string value for AddType
		Write-Warning "Invalid string value '$AddType' provided for -AddType parameter. Ignoring type addition. Use '-AddType' (switch) or '-AddType auto'."
	}
	# If $AddType is $null (not provided), $addTypeFlag remains $false

	# Add the type part if the flag is set
	if ($addTypeFlag) {
		$typePart = "-$type" # Use hyphen as separator based on previous structure
		$logType = "(with type $type)"
	}
	# --- End Type Component Population ---

	# If neither switch is present, $runtimeOrArchPart remains ""

	# --- Assemble Final Name and Log Message (Single Point) ---
	# $versionPart should have been calculated based on $AddVersion before this block
	$assemblyName = $fileNameBase + $versionPart + $runtimeOrArchPart + $typePart

	# Construct log message using the pre-determined parts
	# $logVersion should be set based on $AddVersion before this block
	$publishLogMessage = "Publishing '$($pubxmlFile.BaseName)' $logVersion $logRuntimeOrArch $logType | Profile Runtime: $runtime | Output Assembly: $assemblyName"

	Write-Host $publishLogMessage
	
	# Add /p:UseAppHost=true to ensure an .exe is generated, especially for framework-dependent console apps if needed. Often default for WinExe.
    # Consider adding /p:PublishSingleFile=true or /p:PublishTrimmed=true if desired based on profiles
    dotnet publish /p:PublishProfile=$($pubxmlFile.BaseName) /p:AssemblyName=$assemblyName /p:Configuration=Release # Ensure Release config is passed

    # Check the expected output path based on PublishDir and AssemblyName
	# Define potential output file paths
	$sourceExe = Join-Path $publishDir "$assemblyName.exe"       # Windows executable
	$sourceNoExt = Join-Path $publishDir $assemblyName           # Linux/macOS executable (no extension)
	$sourceDll = Join-Path $publishDir "$assemblyName.dll"       # DLL (less common for executables with current settings)

	# Determine which file actually exists
	$sourceFileToCopy = $null
	$foundFileName = $null

	if (Test-Path $sourceExe) {
		$sourceFileToCopy = $sourceExe
		$foundFileName = "$assemblyName.exe"
	} elseif (Test-Path $sourceNoExt) { # IMPORTANT: Check for no extension *before* DLL
		$sourceFileToCopy = $sourceNoExt
		$foundFileName = $assemblyName
	} elseif (Test-Path $sourceDll) {
		$sourceFileToCopy = $sourceDll
		$foundFileName = "$assemblyName.dll"
	}

	# Copy the file if one was found, otherwise issue a warning
	if ($sourceFileToCopy) {
		try {
			Copy-Item -Path $sourceFileToCopy -Destination $resultsDir -Force -ErrorAction Stop
			Write-Host "Successfully published and copied '$foundFileName' to $resultsDir"
		} catch {
			Write-Error "Error copying '$foundFileName' from '$($sourceFileToCopy)' to '$resultsDir': $($_.Exception.Message)"
		}
	} else {
		# None of the expected formats were found
		Write-Warning "Warning: Could not find published output '$($assemblyName).exe', '$assemblyName' (no extension), or '$($assemblyName).dll' at expected location: '$publishDir'"
		Write-Warning "Check the build output, ensure PublishDir in '$($pubxmlFile.Name)' is correct, and that the AssemblyName parameter passed to dotnet publish matches the actual output file name."
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
# MII6jwYJKoZIhvcNAQcCoII6gDCCOnwCAQExDzANBglghkgBZQMEAgEFADB5Bgor
# BgEEAYI3AgEEoGswaTA0BgorBgEEAYI3AgEeMCYCAwEAAAQQH8w7YFlLCE63JNLG
# KX7zUQIBAAIBAAIBAAIBAAIBADAxMA0GCWCGSAFlAwQCAQUABCCOOOqA3sEO7r4v
# /+WkzWUjZ43FiAYnATVn359LPNH+qaCCIrQwggXMMIIDtKADAgECAhBUmNLR1FsZ
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
# WTpgV4gkSiS4+A09roSdOI4vrRw+p+fL4WrxSK5nMYIXMTCCFy0CAQEwcTBaMQsw
# CQYDVQQGEwJVUzEeMBwGA1UEChMVTWljcm9zb2Z0IENvcnBvcmF0aW9uMSswKQYD
# VQQDEyJNaWNyb3NvZnQgSUQgVmVyaWZpZWQgQ1MgQU9DIENBIDAyAhMzAAM6+rLY
# O+J0xhAzAAAAAzr6MA0GCWCGSAFlAwQCAQUAoF4wEAYKKwYBBAGCNwIBDDECMAAw
# GQYJKoZIhvcNAQkDMQwGCisGAQQBgjcCAQQwLwYJKoZIhvcNAQkEMSIEIL/qT2EW
# eb0rB0zaiDhnc/0htDFET1F3T7qNJPPfcPaHMA0GCSqGSIb3DQEBAQUABIIBgIEL
# Plyh+0qBvsVPC5VIZ/DxhGQ36A0a7s+NuNuQmGFzwDjTLJHq18bo1hgwpq6jePNM
# ZD4nOczpgzFuawZRiyEGXUIaW7RH/hoiJbOG4FxhqeXw54j8xoD862dDdmBHwaSB
# Zey/+ylTJHJMuSbMllV+z9pR+vwSJqop4hBGfG6Mya2kdNrDv6tlhU6HCEp+EZ8F
# RpmNl8NB/PMao/yhYWXPzjBV/VmVWaxp5sFG4pA9f/zIlBV411zx4FO46/Qs8TK0
# jaf5g/ARQec+lNwcksG4qUMA6R2A1Ir7FJ0bQxl7kY3b0WGLv6vE8EUe+3/axTN0
# 7ZMjhDfGO56DwVnBUL8qiDwyXqZTFhD2z52p7R5wEo8VrU/akciX2ETi96VZyVQd
# +B6sshk9OYhVcWT0bW5ADwWtZ6BfInYPuD8KQc1MXNzGK6TgGdvbOXMMEQAUJ/Sh
# chcr/+SUJQt2992ZHBhzrU+WbrXq5RqjTdHeOWJITgAxqdPE+4QMDINoz7OZSKGC
# FLEwghStBgorBgEEAYI3AwMBMYIUnTCCFJkGCSqGSIb3DQEHAqCCFIowghSGAgED
# MQ8wDQYJYIZIAWUDBAIBBQAwggFpBgsqhkiG9w0BCRABBKCCAVgEggFUMIIBUAIB
# AQYKKwYBBAGEWQoDATAxMA0GCWCGSAFlAwQCAQUABCBuwUoJJ8XbL7n0vGRJaGL/
# w+9/3gcJ016QhW5bh+VGewIGZ+U6K7QPGBIyMDI1MDQwNDIwMzQyNC4wN1owBIAC
# AfSggemkgeYwgeMxCzAJBgNVBAYTAlVTMRMwEQYDVQQIEwpXYXNoaW5ndG9uMRAw
# DgYDVQQHEwdSZWRtb25kMR4wHAYDVQQKExVNaWNyb3NvZnQgQ29ycG9yYXRpb24x
# LTArBgNVBAsTJE1pY3Jvc29mdCBJcmVsYW5kIE9wZXJhdGlvbnMgTGltaXRlZDEn
# MCUGA1UECxMeblNoaWVsZCBUU1MgRVNOOjQ5MUEtMDVFMC1EOTQ3MTUwMwYDVQQD
# EyxNaWNyb3NvZnQgUHVibGljIFJTQSBUaW1lIFN0YW1waW5nIEF1dGhvcml0eaCC
# DykwggeCMIIFaqADAgECAhMzAAAABeXPD/9mLsmHAAAAAAAFMA0GCSqGSIb3DQEB
# DAUAMHcxCzAJBgNVBAYTAlVTMR4wHAYDVQQKExVNaWNyb3NvZnQgQ29ycG9yYXRp
# b24xSDBGBgNVBAMTP01pY3Jvc29mdCBJZGVudGl0eSBWZXJpZmljYXRpb24gUm9v
# dCBDZXJ0aWZpY2F0ZSBBdXRob3JpdHkgMjAyMDAeFw0yMDExMTkyMDMyMzFaFw0z
# NTExMTkyMDQyMzFaMGExCzAJBgNVBAYTAlVTMR4wHAYDVQQKExVNaWNyb3NvZnQg
# Q29ycG9yYXRpb24xMjAwBgNVBAMTKU1pY3Jvc29mdCBQdWJsaWMgUlNBIFRpbWVz
# dGFtcGluZyBDQSAyMDIwMIICIjANBgkqhkiG9w0BAQEFAAOCAg8AMIICCgKCAgEA
# nnznUmP94MWfBX1jtQYioxwe1+eXM9ETBb1lRkd3kcFdcG9/sqtDlwxKoVIcaqDb
# +omFio5DHC4RBcbyQHjXCwMk/l3TOYtgoBjxnG/eViS4sOx8y4gSq8Zg49REAf5h
# uXhIkQRKe3Qxs8Sgp02KHAznEa/Ssah8nWo5hJM1xznkRsFPu6rfDHeZeG1Wa1wI
# SvlkpOQooTULFm809Z0ZYlQ8Lp7i5F9YciFlyAKwn6yjN/kR4fkquUWfGmMopNq/
# B8U/pdoZkZZQbxNlqJOiBGgCWpx69uKqKhTPVi3gVErnc/qi+dR8A2MiAz0kN0nh
# 7SqINGbmw5OIRC0EsZ31WF3Uxp3GgZwetEKxLms73KG/Z+MkeuaVDQQheangOEMG
# J4pQZH55ngI0Tdy1bi69INBV5Kn2HVJo9XxRYR/JPGAaM6xGl57Ei95HUw9NV/uC
# 3yFjrhc087qLJQawSC3xzY/EXzsT4I7sDbxOmM2rl4uKK6eEpurRduOQ2hTkmG1h
# SuWYBunFGNv21Kt4N20AKmbeuSnGnsBCd2cjRKG79+TX+sTehawOoxfeOO/jR7wo
# 3liwkGdzPJYHgnJ54UxbckF914AqHOiEV7xTnD1a69w/UTxwjEugpIPMIIE67SFZ
# 2PMo27xjlLAHWW3l1CEAFjLNHd3EQ79PUr8FUXetXr0CAwEAAaOCAhswggIXMA4G
# A1UdDwEB/wQEAwIBhjAQBgkrBgEEAYI3FQEEAwIBADAdBgNVHQ4EFgQUa2koOjUv
# SGNAz3vYr0npPtk92yEwVAYDVR0gBE0wSzBJBgRVHSAAMEEwPwYIKwYBBQUHAgEW
# M2h0dHA6Ly93d3cubWljcm9zb2Z0LmNvbS9wa2lvcHMvRG9jcy9SZXBvc2l0b3J5
# Lmh0bTATBgNVHSUEDDAKBggrBgEFBQcDCDAZBgkrBgEEAYI3FAIEDB4KAFMAdQBi
# AEMAQTAPBgNVHRMBAf8EBTADAQH/MB8GA1UdIwQYMBaAFMh+0mqFKhvKGZgEByfP
# UBBPaKiiMIGEBgNVHR8EfTB7MHmgd6B1hnNodHRwOi8vd3d3Lm1pY3Jvc29mdC5j
# b20vcGtpb3BzL2NybC9NaWNyb3NvZnQlMjBJZGVudGl0eSUyMFZlcmlmaWNhdGlv
# biUyMFJvb3QlMjBDZXJ0aWZpY2F0ZSUyMEF1dGhvcml0eSUyMDIwMjAuY3JsMIGU
# BggrBgEFBQcBAQSBhzCBhDCBgQYIKwYBBQUHMAKGdWh0dHA6Ly93d3cubWljcm9z
# b2Z0LmNvbS9wa2lvcHMvY2VydHMvTWljcm9zb2Z0JTIwSWRlbnRpdHklMjBWZXJp
# ZmljYXRpb24lMjBSb290JTIwQ2VydGlmaWNhdGUlMjBBdXRob3JpdHklMjAyMDIw
# LmNydDANBgkqhkiG9w0BAQwFAAOCAgEAX4h2x35ttVoVdedMeGj6TuHYRJklFaW4
# sTQ5r+k77iB79cSLNe+GzRjv4pVjJviceW6AF6ycWoEYR0LYhaa0ozJLU5Yi+LCm
# crdovkl53DNt4EXs87KDogYb9eGEndSpZ5ZM74LNvVzY0/nPISHz0Xva71QjD4h+
# 8z2XMOZzY7YQ0Psw+etyNZ1CesufU211rLslLKsO8F2aBs2cIo1k+aHOhrw9xw6J
# CWONNboZ497mwYW5EfN0W3zL5s3ad4Xtm7yFM7Ujrhc0aqy3xL7D5FR2J7x9cLWM
# q7eb0oYioXhqV2tgFqbKHeDick+P8tHYIFovIP7YG4ZkJWag1H91KlELGWi3SLv1
# 0o4KGag42pswjybTi4toQcC/irAodDW8HNtX+cbz0sMptFJK+KObAnDFHEsukxD+
# 7jFfEV9Hh/+CSxKRsmnuiovCWIOb+H7DRon9TlxydiFhvu88o0w35JkNbJxTk4Mh
# F/KgaXn0GxdH8elEa2Imq45gaa8D+mTm8LWVydt4ytxYP/bqjN49D9NZ81coE6aQ
# Wm88TwIf4R4YZbOpMKN0CyejaPNN41LGXHeCUMYmBx3PkP8ADHD1J2Cr/6tjuOOC
# ztfp+o9Nc+ZoIAkpUcA/X2gSMkgHAPUvIdtoSAHEUKiBhI6JQivRepyvWcl+JYbY
# bBh7pmgAXVswggefMIIFh6ADAgECAhMzAAAATqPGDj4xw3QnAAAAAABOMA0GCSqG
# SIb3DQEBDAUAMGExCzAJBgNVBAYTAlVTMR4wHAYDVQQKExVNaWNyb3NvZnQgQ29y
# cG9yYXRpb24xMjAwBgNVBAMTKU1pY3Jvc29mdCBQdWJsaWMgUlNBIFRpbWVzdGFt
# cGluZyBDQSAyMDIwMB4XDTI1MDIyNzE5NDAxN1oXDTI2MDIyNjE5NDAxN1owgeMx
# CzAJBgNVBAYTAlVTMRMwEQYDVQQIEwpXYXNoaW5ndG9uMRAwDgYDVQQHEwdSZWRt
# b25kMR4wHAYDVQQKExVNaWNyb3NvZnQgQ29ycG9yYXRpb24xLTArBgNVBAsTJE1p
# Y3Jvc29mdCBJcmVsYW5kIE9wZXJhdGlvbnMgTGltaXRlZDEnMCUGA1UECxMeblNo
# aWVsZCBUU1MgRVNOOjQ5MUEtMDVFMC1EOTQ3MTUwMwYDVQQDEyxNaWNyb3NvZnQg
# UHVibGljIFJTQSBUaW1lIFN0YW1waW5nIEF1dGhvcml0eTCCAiIwDQYJKoZIhvcN
# AQEBBQADggIPADCCAgoCggIBAIbflIu6bAltld7nRX0T6SbF4bEMjoEmU7dL7ZHB
# sOQtg5hiGs8GQlrZVE1yAWzGArehop47rm2Q0Widteu8M0H/c7caoehVD1so8GY0
# Vo12kfImQp1qt5A1kcTcYXWmyQbeLx9w8KHBnIHpesP+sk2STglYsFu3CtHtIFXj
# rLAF7+NjA0Urws3ny5bPd+tjxO6vFIY3V6yXb3GIbcHbfmleNfra4ZEAA/hFxDDd
# m2ReLt/6ij7iVM7Q6EbDQrguRMQydF8HEyLP98iGKHEH36mcz+eJ9Xl/bva+Pk/9
# Yj1aic2MBrA7YTbY/hdw3HSskxvUUgNIcKFQVsz36FSMXQOzVXW1cFXL4UiGqw+y
# lClJcZ0l3H0Aiwsnpvo0t9v4zD5jwJrmeNIlKBeH5EGbfXPelbVEZ2ntMBCgPegB
# 5qelqo+bMfSz9lRTO2c7LByYfQs6UOJL2JhgrZoT+g7WNSEZKXQ+o6DXujpif5XT
# MdMzWCOOiJnMcevpZdD2aYaOEGFXUm51QE2JLKni/71ecZjI6Df4C6vBXRV7WT76
# BYUgcEa08kYbW5kN0jjnBPGFASr9SSnZNGFKQ4J8MyRxEBPZTN33MX9Pz+3ZfZF4
# mS8oyXMCcOmE406M9RSQP9bTVWVuOR0MHo56EpUAK1hVLKfuSD0dEwbGiMawHrel
# OKNBAgMBAAGjggHLMIIBxzAdBgNVHQ4EFgQU8Me6g3SqStL0tyd5iw4rvw1NamIw
# HwYDVR0jBBgwFoAUa2koOjUvSGNAz3vYr0npPtk92yEwbAYDVR0fBGUwYzBhoF+g
# XYZbaHR0cDovL3d3dy5taWNyb3NvZnQuY29tL3BraW9wcy9jcmwvTWljcm9zb2Z0
# JTIwUHVibGljJTIwUlNBJTIwVGltZXN0YW1waW5nJTIwQ0ElMjAyMDIwLmNybDB5
# BggrBgEFBQcBAQRtMGswaQYIKwYBBQUHMAKGXWh0dHA6Ly93d3cubWljcm9zb2Z0
# LmNvbS9wa2lvcHMvY2VydHMvTWljcm9zb2Z0JTIwUHVibGljJTIwUlNBJTIwVGlt
# ZXN0YW1waW5nJTIwQ0ElMjAyMDIwLmNydDAMBgNVHRMBAf8EAjAAMBYGA1UdJQEB
# /wQMMAoGCCsGAQUFBwMIMA4GA1UdDwEB/wQEAwIHgDBmBgNVHSAEXzBdMFEGDCsG
# AQQBgjdMg30BATBBMD8GCCsGAQUFBwIBFjNodHRwOi8vd3d3Lm1pY3Jvc29mdC5j
# b20vcGtpb3BzL0RvY3MvUmVwb3NpdG9yeS5odG0wCAYGZ4EMAQQCMA0GCSqGSIb3
# DQEBDAUAA4ICAQATJnMrpCGuWq9ZOgEKcKyPZj71n/JpX9SYaTK/qOrPsIxzf/qv
# q//uj0dTBnfx7KW0aI1Yz2C6Q78g1b80AU8ARNyoIhmT2SWNI8k7FLo7qeWSzN4b
# cgDgTRSaKGPiWWbJtEjbCgbIkNJ3ZTP9iBJCsxZwv6a45an9ApG1NV/wP8niV0RB
# CH9SIHmD6sv34lxlzHTgGGf1n289fg/LoSMsLFPZ4+G3p0KYu7A5fz616IBk9ZWp
# XQxHFNcSMg/rlwbO65k0k0sRrUlIWkk+71nt2NgpsFaWi2JYq0msX0uzV3LbLaWf
# Kzg1B3ugoSXLypZg3pPypkdXh1wra9h222RuzjyOmwyWi7jTQUBOPZenyapbJhAZ
# XlCxOBaN00bs1V+zUg2miNte9E8CWHagq+Rts/1iSiPCwWmMKfqilSSdSMtYSXMy
# ciCKWexeRjAX0QovSsGv0pMqkYfPa5ubnI03ab/A6Kod2TEF8ufShV9sQSqbDscM
# W12TQOboyTUhc8wPp8p2WWejvrH+9AUO6hToaYeM4jMmmOcAAlpHm2AY+GAk+Y54
# d6DYA6NBED+CSEFSakUVRqNbkN4mN1SOklodZhvRphmF9Ot0DuzLu/KByWIfHbaY
# /wTusrEVGH4W4n39FmcMIvVbMpeOENZ59+xGiFwt5izuabZiHN/EFR4leTGCA9Qw
# ggPQAgEBMHgwYTELMAkGA1UEBhMCVVMxHjAcBgNVBAoTFU1pY3Jvc29mdCBDb3Jw
# b3JhdGlvbjEyMDAGA1UEAxMpTWljcm9zb2Z0IFB1YmxpYyBSU0EgVGltZXN0YW1w
# aW5nIENBIDIwMjACEzMAAABOo8YOPjHDdCcAAAAAAE4wDQYJYIZIAWUDBAIBBQCg
# ggEtMBoGCSqGSIb3DQEJAzENBgsqhkiG9w0BCRABBDAvBgkqhkiG9w0BCQQxIgQg
# zQIoL69ZCBr7L5Ie7appyokBHCNmFgEypalQ5Ap8bAcwgd0GCyqGSIb3DQEJEAIv
# MYHNMIHKMIHHMIGgBCBvsqfT7ygFHbDe/Tj3QCn25JHDuUhgytNPRb67/5ZKFzB8
# MGWkYzBhMQswCQYDVQQGEwJVUzEeMBwGA1UEChMVTWljcm9zb2Z0IENvcnBvcmF0
# aW9uMTIwMAYDVQQDEylNaWNyb3NvZnQgUHVibGljIFJTQSBUaW1lc3RhbXBpbmcg
# Q0EgMjAyMAITMwAAAE6jxg4+McN0JwAAAAAATjAiBCCT1OrKp73/Yzob/NPOikig
# aVFRhHGj9eRClEDIs0ro/jANBgkqhkiG9w0BAQsFAASCAgB2MMoUTrhas/Q0plqU
# 00ZCj15pBcIUY2q5/5n9uetU8GsYoKqLaUfLWzDweqiKr0XLFeiawJBpd5J33ITe
# XsT0DTQKdcqjIuKaqmbiuzLQQV3EiQbZzHeEjTpujK1iXzWWxli4A8u+T8U+mIrW
# V6IUPS/MAj1CR5wH8VM2hG9Lb2eybZPsBTsLu87vYF3O6ALh4MDhUseWe3qxzynK
# arqsVn0GejnF+OtMMz3F6/0/O2Iis3xoALa8cWW08LgbXDf5JA0u93cZuR6MwbZd
# /MZ1swSFqwwACnxi+akp8CDyAm9D+v7TlHGNzdb2ls/P90N2Ajj1G6X90pTb4jle
# Y+Ust9uYoY8GW3x41BpKy5JAvFCTHyv85N9I+aV13Cze61Cb2twqJHrwAVQLqIjP
# j0aE6+2wDtKipicmdSgRy+mWnmJ93NcnhTCRMnyKQWCYuy5z29jT3nMInqPXE3YV
# DP6yQx3H9YQ4xd0zNcSAN8VqF1ZElY9nfndsv3mHrG+BLhZCZiIOR33uwRUVkHpc
# Ek7LHGv0XbPiiBaHsMG787tvG0LsFIS7mx+pnjABDbdkpYi1dqEdwgnjvUy/DY9p
# Hc6IbQSqDFrFJhvfNfZGqDE4l78MGwdJo0j9lQQRdgbwG+7TPfo7PnZT6FnFTSWP
# CEFx7fLIr8qBr8Cd0DzB2a7HjQ==
# SIG # End signature block
