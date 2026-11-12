import { InputNumber, Slider } from 'antd';

type Props = {
    label: string;
    id?: string;
    value: number;
    min: number;
    max: number;
    disabled: boolean;
    onChange: (value: number) => void;
};

export default function NumericSlider({ label, id, value, min, max, disabled, onChange }: Props) {
    const update = (next: number | null) => {
        if (!disabled && next !== null && Number.isFinite(next)) onChange(next);
    };
    return <div className="numeric-slider">
        <Slider ariaLabelForHandle={`${label}滑块`} min={min} max={max} step={1}
            value={value} disabled={disabled} onChange={update} />
        <InputNumber id={id} aria-label={label} size="small" min={min} max={max}
            step={1} precision={0} value={value} disabled={disabled} onChange={update} />
    </div>;
}
