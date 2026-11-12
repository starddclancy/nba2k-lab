import { Tag } from 'antd';
import type { HeadScanInfo } from '../types';

export default function HeadScanTag({ info }: { info?: HeadScanInfo }) {
    const status = info?.status ?? 'unavailable';
    const label = status === 'scanned' ? '扫描头' : status === 'not_scanned' ? '非扫描头' : '未知';
    const title = info?.flag != null
        ? `ISSCANNEDHEAD = ${info.flag}，仅表示扫描头标记，未验证真人脸或泥人分类`
        : info?.error ?? '扫描头标记不可读';
    return (
        <Tag
            className="head-scan-tag"
            data-status={status}
            color={status === 'scanned' ? 'green' : 'default'}
            title={title}
            style={{ marginInlineEnd: 0 }}
        >
            {label}
        </Tag>
    );
}
