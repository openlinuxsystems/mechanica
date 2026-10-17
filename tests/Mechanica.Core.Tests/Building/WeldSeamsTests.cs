using Mechanica.Core.Building;
using Mechanica.Core.Creations;
using Mechanica.Core.Data;
using static Mechanica.Core.Tests.Creations.TestParts;

namespace Mechanica.Core.Tests.Building;

public class WeldSeamsTests
{
    [Fact]
    public void TouchingCellsShareAFace()
    {
        var faces = WeldSeams.Between([GridCell.Zero, new GridCell(0, 1, 0)], [new GridCell(1, 0, 0), new GridCell(1, 1, 0), new GridCell(5, 0, 0)]);

        Assert.Equal([new SeamFace(GridCell.Zero, new GridCell(1, 0, 0)), new SeamFace(new GridCell(0, 1, 0), new GridCell(1, 0, 0))], faces);
        Assert.Equal(new Vec3(0.125f, 0, 0), faces[0].CentreMetres);
    }

    [Fact]
    public void ApartOrOverlappingCellsHaveNoSeam()
    {
        Assert.Empty(WeldSeams.Between([GridCell.Zero], [new GridCell(2, 0, 0), new GridCell(1, 1, 0)]));
        Assert.Empty(WeldSeams.Between([GridCell.Zero], [GridCell.Zero]));
    }

    [Fact]
    public void TwoPartsOfACreation()
    {
        var creation = new Creation(1, Catalog());
        creation.Place(Beam, GridCell.Zero);
        creation.Place(Block, new GridCell(0, 1, 0));

        var face = Assert.Single(WeldSeams.Between(creation, 1, 2));

        Assert.Equal(new SeamFace(GridCell.Zero, new GridCell(0, 1, 0)), face);
    }
}
