"""One Windows-user profile gate shared by launcher and LAN writer processes."""
import os,time
from contextlib import contextmanager
from pathlib import Path


class ProfileBusy(RuntimeError):pass


@contextmanager
def hold(lab,timeout=0):
    path=Path(lab)/'profile-access.lock';path.parent.mkdir(parents=True,exist_ok=True)
    stream=path.open('a+b')
    if path.stat().st_size==0:stream.write(b'0');stream.flush()
    deadline=time.monotonic()+timeout;locked=False
    try:
        while True:
            try:
                stream.seek(0)
                if os.name=='nt':
                    import msvcrt
                    msvcrt.locking(stream.fileno(),msvcrt.LK_NBLCK,1)
                else:
                    import fcntl
                    fcntl.flock(stream.fileno(),fcntl.LOCK_EX|fcntl.LOCK_NB)
                locked=True;break
            except OSError:
                if time.monotonic()>=deadline:raise ProfileBusy('Traktor oder ein anderer Abgleich verwendet das Profil')
                time.sleep(.05)
        yield
    finally:
        if locked:
            stream.seek(0)
            if os.name=='nt':msvcrt.locking(stream.fileno(),msvcrt.LK_UNLCK,1)
            else:fcntl.flock(stream.fileno(),fcntl.LOCK_UN)
        stream.close()
