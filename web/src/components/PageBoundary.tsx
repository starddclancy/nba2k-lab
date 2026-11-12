import { Component } from 'react';
import type { ErrorInfo, ReactNode } from 'react';
import { Button, Result } from 'antd';

/**
 * Keeps a render error inside one page. Without it React unmounts the whole tree and the window
 * goes blank. Shows the message, logs the component stack, and resets when the page changes.
 */
export default class PageBoundary extends Component<{ page: string; children: ReactNode }, { error: Error | null; stack: string }> {
    state = { error: null as Error | null, stack: '' };

    static getDerivedStateFromError(error: Error) {
        return { error };
    }

    componentDidCatch(error: Error, info: ErrorInfo) {
        this.setState({ stack: info.componentStack ?? '' });
        console.error(`页面「${this.props.page}」渲染出错`, error, info.componentStack);
    }

    componentDidUpdate(previous: { page: string }) {
        if (previous.page !== this.props.page && this.state.error) {
            this.setState({ error: null, stack: '' });
        }
    }

    render() {
        if (!this.state.error) return this.props.children;
        return <div className="page-error">
            <Result status="warning" title="这个页面出错了，其他页面不受影响"
                subTitle={this.state.error.message}
                extra={<Button type="primary" onClick={() => this.setState({ error: null, stack: '' })}>重新加载此页</Button>} />
            {this.state.stack && <pre>{this.state.stack.trim().split('\n').slice(0, 6).join('\n')}</pre>}
        </div>;
    }
}
