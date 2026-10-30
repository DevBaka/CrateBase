import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch
import setup_windows as setup


class InstallerTests(unittest.TestCase):
    def test_arc_and_amd_select_directml_without_nvidia_tools(self):
        for vendor in ('PCI\\VEN_8086&DEV_7D55', 'PCI\\VEN_1002&DEV_744C'):
            with self.subTest(vendor=vendor), patch.object(setup, 'run', return_value=vendor):
                self.assertEqual(setup.choose_backend('auto'), 'directml')

    def test_nvidia_selects_cuda_and_explicit_choice_wins(self):
        with patch.object(setup, 'run', return_value='PCI\\VEN_8086\nPCI\\VEN_10DE'):
            self.assertEqual(setup.choose_backend('auto'), 'cuda')
        with patch.object(setup, 'run') as command:
            self.assertEqual(setup.choose_backend('cpu'), 'cpu')
            command.assert_not_called()

    def test_unknown_adapter_uses_cpu(self):
        with patch.object(setup, 'run', return_value='Microsoft Basic Display Adapter'):
            self.assertEqual(setup.choose_backend('auto'), 'cpu')

    def test_repeat_setup_probes_but_does_not_reinstall(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            python = root / 'Scripts/python.exe'
            python.parent.mkdir()
            python.touch()
            with patch.object(setup, 'run') as command:
                setup.environment('uv', root, ['package==1'], 'import package')
                self.assertTrue(any('install' in call.args for call in command.call_args_list))
            with patch.object(setup, 'run') as command:
                setup.environment('uv', root, ['package==1'], 'import package')
                self.assertEqual(command.call_count, 2)
                self.assertFalse(any('install' in call.args for call in command.call_args_list))


if __name__ == '__main__':
    unittest.main()
