import { cenarios, type Cenario } from '../cenarios';

interface Props {
  cenario: Cenario;
  onEscolher: (cenario: Cenario) => void;
}

const fases = [
  { fase: 1, rotulo: 'Fase 1: fila propostas' },
  { fase: 2, rotulo: 'Fase 2: tópico apolice-emitida' },
  { fase: 3, rotulo: 'Fase 3: sessions, duplicatas e agendamento' },
  { fase: 4, rotulo: 'Fase 4: API REST + outbox (SQL Server)' },
] as const;

export function SeletorDeCenario({ cenario, onEscolher }: Props) {
  return (
    <label className="campo campo--cenario">
      <span className="campo__rotulo">Cenário</span>
      <select
        className="campo__controle"
        value={cenario.id}
        onChange={(e) => onEscolher(cenarios.find((c) => c.id === e.target.value)!)}
      >
        {fases.map(({ fase, rotulo }) => (
          <optgroup key={fase} label={rotulo}>
            {cenarios
              .filter((c) => c.fase === fase)
              .map((c) => (
                <option key={c.id} value={c.id}>
                  {c.titulo}
                </option>
              ))}
          </optgroup>
        ))}
      </select>
    </label>
  );
}
