namespace Mechanica.Core.Items;

/// <summary>One queued order at a station: <see cref="Count"/> more crafts of a recipe.</summary>
/// <param name="Recipe">Qualified recipe id.</param>
/// <param name="Count">Crafts still to finish (1 or more). Their inputs were already taken.</param>
/// <param name="Progress">Seconds of work done on the current craft, at speed 1.</param>
public sealed record CraftJob(string Recipe, int Count, float Progress = 0f);

/// <summary>
/// A crafting station's orders, worked first to last. The inputs of every craft are taken when the order
/// is queued; each finished craft puts its outputs in the station's inventory. A craft whose outputs don't
/// fit waits, finished, until there is room. <see cref="Revision"/> goes up on every change, including
/// progress, so sync can tell when to send it again.
/// </summary>
public sealed class CraftingQueue
{
    private readonly List<CraftJob> _jobs = [];

    /// <summary>Orders, the one being worked first.</summary>
    public IReadOnlyList<CraftJob> Jobs => _jobs;

    /// <summary>Goes up by one on every change.</summary>
    public int Revision { get; private set; }

    /// <summary>True when there is nothing to do.</summary>
    public bool IsEmpty => _jobs.Count == 0;

    /// <summary>True when the first order's current craft is done but its outputs don't fit.</summary>
    public bool IsBlocked { get; private set; }

    /// <summary>Crafts finished since the queue was made (not saved): a part script watches it to see a craft complete.</summary>
    public int Finished { get; private set; }

    /// <summary>Adds an order at the end.</summary>
    public void Enqueue(CraftJob job)
    {
        if (job.Count < 1)
            throw new ArgumentOutOfRangeException(nameof(job), "A job needs a count of 1 or more.");
        _jobs.Add(job);
        Revision++;
    }

    /// <summary>Removes the order at <paramref name="index"/> and returns it.</summary>
    public CraftJob RemoveAt(int index)
    {
        var job = _jobs[index];
        _jobs.RemoveAt(index);
        if (index == 0)
            IsBlocked = false;
        Revision++;
        return job;
    }

    /// <summary>Empties the queue.</summary>
    public void Clear()
    {
        if (_jobs.Count == 0)
            return;
        _jobs.Clear();
        IsBlocked = false;
        Revision++;
    }

    /// <summary>
    /// Works for <paramref name="seconds"/> at <paramref name="speed"/>, finishing as many crafts as the time
    /// allows into <paramref name="output"/>. An order whose recipe no longer exists is dropped. Returns the
    /// number of crafts finished.
    /// </summary>
    public int Tick(float seconds, float speed, RecipeBook recipes, Inventory output) => Tick(seconds, speed, recipes, [output]);

    /// <summary>
    /// As <see cref="Tick(float, float, RecipeBook, Inventory)"/>, putting each craft's outputs in the first of
    /// <paramref name="outputs"/> with room, then the next (<see cref="ItemPool"/>): a station's linked storage, then its own slots.
    /// </summary>
    public int Tick(float seconds, float speed, RecipeBook recipes, IReadOnlyList<Inventory> outputs)
    {
        if (_jobs.Count == 0 || !(seconds > 0))
            return 0;
        var work = seconds * speed;
        var finished = 0;
        while (_jobs.Count > 0)
        {
            var job = _jobs[0];
            if (recipes.Find(job.Recipe) is not { } recipe)
            {
                RemoveAt(0);
                continue;
            }
            var needed = recipe.Time - job.Progress;
            if (work < needed)
            {
                _jobs[0] = job with { Progress = job.Progress + work };
                Revision++;
                break;
            }
            if (!ItemPool.TryAddAll(outputs, [.. recipe.Outputs]))
            {
                if (!IsBlocked || job.Progress != recipe.Time)
                {
                    _jobs[0] = job with { Progress = recipe.Time };
                    IsBlocked = true;
                    Revision++;
                }
                break;
            }
            IsBlocked = false;
            work -= Math.Max(0, needed);
            finished++;
            Finished++;
            if (job.Count == 1)
                _jobs.RemoveAt(0);
            else
                _jobs[0] = job with { Count = job.Count - 1, Progress = 0 };
            Revision++;
        }
        return finished;
    }

    /// <summary>Replaces every order (loading saves, sync views).</summary>
    public void Load(IEnumerable<CraftJob> jobs)
    {
        _jobs.Clear();
        _jobs.AddRange(jobs);
        IsBlocked = false;
        Revision++;
    }
}
