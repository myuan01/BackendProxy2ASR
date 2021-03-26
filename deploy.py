import os
import subprocess
import sys
from pathlib import Path
from zipfile import ZipFile, ZipInfo

USAGE_TEXT = "Usage: python deploy.py <path to zip file> <config file>"
PROGRAM_NAME = "BackendProxy2ASR"


class ZipFileWithPermissions(ZipFile):
    """ Custom ZipFile class handling file permissions.
    """

    def _extract_member(self, member, targetpath, pwd):
        if not isinstance(member, ZipInfo):
            member = self.getinfo(member)

        targetpath = super()._extract_member(member, targetpath, pwd)

        attr = member.external_attr >> 16
        if attr != 0:
            os.chmod(targetpath, attr)
        return targetpath


if __name__ == "__main__":
    assert len(sys.argv) == 3, f"Invalid arguments. {USAGE_TEXT}"

    _, zip_path, config_path = sys.argv
    zip_path = Path(zip_path)

    assert zip_path.exists(), f"{zip_path} does not exist"
    assert zip_path.suffix == ".zip", f"{zip_path} is not a zip file"

    assert Path(config_path).exists(), f"{config_path} does not exist"

    unzip_dir = Path(zip_path.stem)

    with ZipFileWithPermissions(zip_path, 'r') as zip_ref:
        print(f"unzipping {zip_path}")
        zip_ref.extractall(unzip_dir)

    backend_dll_list = list(unzip_dir.rglob(f"{PROGRAM_NAME}.dll"))

    assert len(backend_dll_list) != 0, f"Unable to find {PROGRAM_NAME}.dll"

    backend_dll = backend_dll_list[0]

    subprocess.run(["dotnet", str(backend_dll), config_path])
