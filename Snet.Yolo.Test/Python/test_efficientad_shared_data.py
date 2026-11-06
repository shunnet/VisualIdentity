"""EfficientAD 通用数据只准备一次；下载或解压失败不能发布半成品缓存。"""
from concurrent.futures import ThreadPoolExecutor
from functools import partial
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from types import SimpleNamespace
import hashlib
import io
import tarfile
import threading
from urllib.error import URLError
from unittest.mock import patch

from PIL import Image
import pytest
from anomalib.data.utils import DownloadInfo
from anomalib.models.image.efficient_ad import lightning_model

from test_anomalib_early_stopping import load_pipeline


def dataset_archive():
    """创建遵循真实 ImageNette 目录结构的极小归档，避免测试下载真实数据。"""
    image = io.BytesIO()
    Image.new('RGB', (8, 8), 'red').save(image, format='JPEG')
    content = image.getvalue()
    output = io.BytesIO()
    with tarfile.open(fileobj=output, mode='w:gz') as archive:
        for split in ['train', 'val']:
            entry = tarfile.TarInfo(f'imagenette2/{split}/n01440764/sample.JPEG')
            entry.size = len(content)
            archive.addfile(entry, io.BytesIO(content))
    return output.getvalue()


@pytest.fixture
def endpoint(monkeypatch):
    """用真实本机 HTTP 响应覆盖网络读取、重试、摘要和落盘，不访问公网。"""
    monkeypatch.setenv('no_proxy', '127.0.0.1')
    content = dataset_archive()
    state = SimpleNamespace(content=content, failures=0, requests=0, truncated=False)

    class Handler(BaseHTTPRequestHandler):
        def do_GET(self):
            state.requests += 1
            if state.requests <= state.failures:
                self.send_error(503)
                return
            self.send_response(200)
            self.send_header('Content-Length', str(len(state.content)))
            self.end_headers()
            self.wfile.write(state.content[:-16] if state.truncated else state.content)

        def log_message(self, *args):
            pass

    server = ThreadingHTTPServer(('127.0.0.1', 0), Handler)
    worker = threading.Thread(target=partial(server.serve_forever, poll_interval=.01), daemon=True)
    worker.start()
    state.info = DownloadInfo(name='imagenette2.tgz',
        url=f'http://127.0.0.1:{server.server_port}/imagenette2.tgz', hashsum=hashlib.sha256(content).hexdigest())
    monkeypatch.setattr(lightning_model, 'IMAGENETTE_DOWNLOAD_INFO', state.info)
    monkeypatch.setattr('time.sleep', lambda _: None)
    try:
        yield state
    finally:
        server.shutdown()
        server.server_close()
        worker.join(timeout=5)


def prepare(pipeline, root):
    """确保红灯来自产品缺少共享准备流程，而不是测试拼写错误。"""
    function = getattr(pipeline, 'prepare_efficientad_data', None)
    assert callable(function), 'EfficientAD still relies on a per-run relative dataset directory'
    return function(root)


def test_complete_dataset_is_reused_across_project_working_directories(tmp_path, monkeypatch, endpoint):
    """切换项目工作目录后仍复用同一份已完成数据，不再次联网。"""
    pipeline = load_pipeline(tmp_path)
    shared = tmp_path/'shared'
    project_a, project_b = tmp_path/'project-a', tmp_path/'project-b'
    project_a.mkdir(); project_b.mkdir()
    monkeypatch.chdir(project_a)
    first = prepare(pipeline, shared)
    monkeypatch.chdir(project_b)
    second = prepare(pipeline, shared)
    assert first == second == shared/'dataset'
    assert endpoint.requests == 1
    assert len(list(first.rglob('*.JPEG'))) == 2
    assert (shared/'imagenette2.tgz').read_bytes() == endpoint.content
    model = pipeline.create_model('efficient_ad_small', 128, first)
    assert model.imagenet_dir == first


def test_verified_archive_can_prepare_offline_without_downloading(tmp_path, endpoint):
    """手动放入完整归档后只校验并解压；归档保留，以便缺失数据时重建。"""
    pipeline = load_pipeline(tmp_path)
    shared = tmp_path/'shared'; shared.mkdir()
    (shared/'imagenette2.tgz').write_bytes(endpoint.content)
    result = prepare(pipeline, shared)
    assert result.is_dir() and endpoint.requests == 0
    assert (shared/'imagenette2.tgz').is_file()


def test_transient_connection_failure_has_bounded_retries(tmp_path, endpoint):
    """与用户现场相同的连接被拒绝应有限重试，并提示网址及离线放置位置。"""
    pipeline = load_pipeline(tmp_path)
    shared = tmp_path/'shared'
    with patch('urllib.request.urlopen', side_effect=URLError(ConnectionRefusedError(111, 'Connection refused'))) as open_url:
        with pytest.raises(RuntimeError) as error:
            prepare(pipeline, shared)
    assert open_url.call_count == 3
    assert endpoint.info.url in str(error.value) and str(shared/'imagenette2.tgz') in str(error.value)
    assert not (shared/'dataset').exists()
    assert not list(shared.glob('.extract-*'))


def test_temporary_http_failure_then_success(tmp_path, endpoint):
    """HTTP 503 恢复后可完成准备，并且只发布一次完整数据。"""
    endpoint.failures = 2
    result = prepare(load_pipeline(tmp_path), tmp_path/'shared')
    assert result.is_dir() and endpoint.requests == 3


def test_truncated_response_never_becomes_a_usable_cache(tmp_path, endpoint):
    """响应中途断开时，不能留下最终归档或可复用的数据目录。"""
    endpoint.truncated = True
    shared = tmp_path/'shared'
    with pytest.raises(RuntimeError):
        prepare(load_pipeline(tmp_path), shared)
    assert endpoint.requests == 3
    assert not (shared/'dataset').exists() and not (shared/'imagenette2.tgz').exists()


def test_bad_download_digest_is_rejected(tmp_path, endpoint):
    """即使 HTTP 返回成功，也必须拒绝与官方摘要不符的响应。"""
    endpoint.content = b'wrong archive from gateway'
    shared = tmp_path/'shared'
    with pytest.raises(RuntimeError, match='SHA-256'):
        prepare(load_pipeline(tmp_path), shared)
    assert not (shared/'dataset').exists() and not (shared/'imagenette2.tgz').exists()


def test_incomplete_dataset_and_corrupt_archive_are_repaired(tmp_path, endpoint):
    """已有空目录不能冒充成功缓存；损坏归档须重新获取并校验。"""
    shared = tmp_path/'shared'; (shared/'dataset').mkdir(parents=True)
    (shared/'imagenette2.tgz').write_bytes(b'partial download')
    result = prepare(load_pipeline(tmp_path), shared)
    assert endpoint.requests == 1 and len(list(result.rglob('*.JPEG'))) == 2


def test_extract_failure_preserves_archive_and_retries_without_network(tmp_path, endpoint):
    """解压失败保留已验证归档；下一次重建不需要重复下载。"""
    pipeline = load_pipeline(tmp_path)
    shared = tmp_path/'shared'
    with patch('anomalib.data.utils.download.extract', side_effect=OSError('disk full')):
        with pytest.raises(OSError, match='disk full'):
            prepare(pipeline, shared)
    assert (shared/'imagenette2.tgz').is_file() and not (shared/'dataset').exists()
    assert not list(shared.glob('.extract-*'))
    assert prepare(pipeline, shared).is_dir() and endpoint.requests == 1


def test_concurrent_preparations_share_one_download(tmp_path, endpoint):
    """两个准备任务通过跨进程文件锁共享同一份归档与已发布数据。"""
    pipeline = load_pipeline(tmp_path)
    shared = tmp_path/'shared'
    with ThreadPoolExecutor(max_workers=2) as pool:
        paths = list(pool.map(lambda _: prepare(pipeline, shared), range(2)))
    assert paths[0] == paths[1] and endpoint.requests == 1
